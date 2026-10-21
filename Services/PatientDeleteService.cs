using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VaccineAPI.Models;

namespace VaccineAPI.Services
{
    // Patient delete without orphans. A patient who has given doses or invoices cannot just be
    // removed: the stock those doses consumed and the money their invoices carry would be left with
    // no owner (blank-name rows in Payment Reconciliation, stock that no patient explains).
    //
    //   Preview()  -> what the patient leaves behind (given doses, invoices, open assignments)
    //   Reverse()  -> puts consumed stock back through the ledger writer and cancels cash-less
    //                 assignments. It deliberately does NOT touch invoices or payments: those stay
    //                 in Payment Reconciliation tagged "Patient deleted" until the doctor cancels
    //                 them explicitly (PaCashHandoverController.CancelDeletedPatientInvoice).
    //   AddAudit() -> one paactivitylogs row (ActionCode "PatientDeleted"), written in the same
    //                 SaveChanges as the delete. Its Notes JSON carries the name snapshot that
    //                 reconciliation uses to name the row once the patient no longer exists.
    public static class PatientDeleteService
    {
        public const string AuditCode = "PatientDeleted";

        public class GivenDose
        {
            public long ScheduleId { get; set; }
            public string Dose { get; set; } = "";
            public string Brand { get; set; } = "";
            public string GivenDate { get; set; } = "";
            public bool ConsumedStock { get; set; }
        }

        public class InvoiceLine
        {
            public long InvoiceId { get; set; }
            public decimal Amount { get; set; }
            public string PaymentMode { get; set; } = "";
            public string PaName { get; set; } = "";
            public bool IsConfirmed { get; set; }
            public string? ConfirmedAt { get; set; }
            public bool PendingHandover { get; set; }
        }

        public class Footprint
        {
            public long ChildId { get; set; }
            public string Name { get; set; } = "";
            public List<GivenDose> GivenDoses { get; set; } = new List<GivenDose>();
            public List<InvoiceLine> Invoices { get; set; } = new List<InvoiceLine>();
            public int OpenAssignments { get; set; }
            public bool HasFootprint => GivenDoses.Count > 0 || Invoices.Count > 0;
            public decimal InvoiceTotal => Invoices.Sum(i => i.Amount);
            public int ConfirmedInvoices => Invoices.Count(i => i.IsConfirmed);
        }

        public static Footprint Preview(Context db, Child child)
        {
            var fp = new Footprint { ChildId = child.Id, Name = child.Name ?? "" };
            var inv = new InventoryTransactionService(db);

            var given = db.Schedules.Where(s => s.ChildId == child.Id && s.IsDone == true)
                .Select(s => new { s.Id, DoseName = s.Dose.Name, s.BrandId, s.GivenDate })
                .ToList();
            var brandIds = given.Where(g => g.BrandId.HasValue).Select(g => g.BrandId!.Value).Distinct().ToList();
            var brandNames = db.Brands.Where(b => brandIds.Contains(b.Id)).ToList()
                .ToDictionary(b => b.Id, b => b.Name ?? "");
            foreach (var g in given)
            {
                fp.GivenDoses.Add(new GivenDose
                {
                    ScheduleId = g.Id,
                    Dose = g.DoseName ?? "",
                    Brand = g.BrandId.HasValue && brandNames.ContainsKey(g.BrandId.Value) ? brandNames[g.BrandId.Value] : "",
                    GivenDate = g.GivenDate.HasValue ? g.GivenDate.Value.ToString("yyyy-MM-dd") : "",
                    ConsumedStock = g.BrandId.HasValue && inv.HasLiveGive(g.Id)
                });
            }

            var invoices = db.InvoiceSubmissions
                .Where(i => i.ChildId == child.Id && i.InvoiceStatus != "Cancelled" && i.TotalAmount > 0)
                .ToList();
            var paIds = invoices.Where(i => i.PaId.HasValue).Select(i => i.PaId!.Value).Distinct().ToList();
            var paNames = db.PersonalAssistant.Where(p => paIds.Contains(p.Id)).ToList()
                .ToDictionary(p => p.Id, p => p.Name ?? "");
            foreach (var i in invoices)
            {
                fp.Invoices.Add(new InvoiceLine
                {
                    InvoiceId = i.Id,
                    Amount = i.TotalAmount,
                    PaymentMode = i.PaymentMode ?? "",
                    PaName = i.PaId.HasValue && paNames.ContainsKey(i.PaId.Value) ? paNames[i.PaId.Value] : "",
                    IsConfirmed = i.IsConfirmedByDoctor,
                    ConfirmedAt = i.ConfirmedAt.HasValue ? i.ConfirmedAt.Value.ToString("yyyy-MM-dd") : null,
                    PendingHandover = i.PendingHandover
                });
            }

            fp.OpenAssignments = db.PAAssignments.Count(a => a.ChildId == child.Id && !a.IsCancelled && !a.IsCompleted);
            return fp;
        }

        // Puts every consumed unit back (ledger Unadminister on the give's own batch) and cancels the
        // child's assignments that carry no invoice. Caller saves. Invoices and payments are untouched.
        public static void Reverse(Context db, Child child, Footprint fp)
        {
            var inv = new InventoryTransactionService(db);
            foreach (var d in fp.GivenDoses.Where(d => d.ConsumedStock))
            {
                var s = db.Schedules.First(x => x.Id == d.ScheduleId);
                var clinicId = inv.LiveGiveClinicId(s.Id) ?? child.ClinicId;
                inv.UnadministerSync(DoctorIdOf(db, child), clinicId, s.BrandId!.Value, s.Id,
                    s.GivenDate ?? DateTime.Today);
            }

            var invoicedIds = db.InvoiceSubmissions
                .Where(i => i.ChildId == child.Id && i.InvoiceStatus != "Cancelled" && i.TotalAmount > 0)
                .Select(i => i.Id).ToList();
            var open = db.PAAssignments
                .Where(a => a.ChildId == child.Id && !a.IsCancelled && !a.IsCompleted
                         && !(a.InvoiceSubmissionId.HasValue && invoicedIds.Contains(a.InvoiceSubmissionId.Value)))
                .ToList();
            foreach (var a in open)
            {
                a.IsCancelled = true;
                a.CancelledAt = DateTime.UtcNow;
                a.CancelReason = "Patient deleted";
                AssignmentAudit.Add(db, "Cancelled", a, "DOCTOR", a.DoctorId,
                    "Assignment cancelled: patient deleted", "Patient deleted");
            }
        }

        public static long DoctorIdOf(Context db, Child child)
            => db.Clinics.Where(c => c.Id == child.ClinicId).Select(c => c.DoctorId).FirstOrDefault();

        public static void AddAudit(Context db, Child child, Footprint fp, string actorType, long actorId,
                                    string mode)
        {
            var env = new JObject
            {
                ["name"] = child.Name,
                ["guardian"] = child.Guardian,
                ["fatherName"] = child.FatherName,
                ["dob"] = child.DOB.ToString("yyyy-MM-dd"),
                ["actor"] = actorType,
                ["actorId"] = actorId,
                ["mode"] = mode,                              // "none" | "reverse"
                ["stockRestoredDoses"] = fp.GivenDoses.Count(d => d.ConsumedStock && mode == "reverse"),
                ["invoices"] = JToken.FromObject(fp.Invoices)
            };
            db.PaActivityLogs.Add(new PaActivityLog
            {
                PaId = actorType == "PA" ? (long?)actorId : null,
                DoctorId = DoctorIdOf(db, child),
                ClinicId = child.ClinicId,
                PatientId = child.Id,
                ActionCode = AuditCode,
                Description = $"Patient deleted: {child.Name}",
                Notes = env.ToString(Formatting.None),
                ActionDate = DateTime.UtcNow
            });
        }

        public class Snapshot
        {
            public string Name { get; set; } = "";
            public DateTime At { get; set; }
            public string Actor { get; set; } = "";
        }

        // childId -> who deleted it and the name it had; absent when the delete predates this audit.
        public static Dictionary<long, Snapshot> Snapshots(Context db, ICollection<long> childIds)
        {
            var result = new Dictionary<long, Snapshot>();
            if (childIds.Count == 0) return result;
            var rows = db.PaActivityLogs
                .Where(l => l.ActionCode == AuditCode && l.PatientId.HasValue && childIds.Contains(l.PatientId.Value))
                .OrderBy(l => l.ActionDate).ToList();
            foreach (var r in rows)
            {
                string name = "", actor = "";
                try
                {
                    var e = JObject.Parse(r.Notes ?? "{}");
                    name = e.Value<string>("name") ?? "";
                    actor = e.Value<string>("actor") ?? "";
                }
                catch { }
                result[r.PatientId!.Value] = new Snapshot { Name = name, At = r.ActionDate, Actor = actor };
            }
            return result;
        }
    }
}
