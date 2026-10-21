using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VaccineAPI.Models;

namespace VaccineAPI.Services
{
    // Who did what to a PA assignment, when and why (PA_TRACKING_MERGE_SPEC.md Q9).
    //
    // Rows go into the existing paactivitylogs table, so no schema change is needed to deploy: the
    // ActionCode starts with "Asg", PatientId is the child, and Notes carries a JSON envelope with
    // the assignment id, the actor, the reason and any before/after values. The actor is PaId for a
    // PA, ManagerId for a manager, and neither for the doctor (DoctorId is always set).
    //
    // Add() only adds the row to the change tracker: callers write it in the SAME SaveChanges as the
    // action, so an action and its log entry commit or fail together. (A dedicated AssignmentId
    // column would be cleaner; it needs a hand-written SQL file and a deploy order, so it is a
    // separate later change.)
    public static class AssignmentAudit
    {
        public const string CodePrefix = "Asg";

        public static void Add(Context db, string action, PAAssignment a, string actorType, long actorId,
                               string description, string? reason = null, object? data = null)
        {
            var envelope = new JObject
            {
                ["assignmentId"] = a.Id,
                ["actor"] = actorType,
                ["actorId"] = actorId,
                ["reason"] = string.IsNullOrWhiteSpace(reason) ? null : reason,
                ["data"] = data == null ? null : JToken.FromObject(data)
            };
            db.PaActivityLogs.Add(new PaActivityLog
            {
                PaId = actorType == "PA" ? (long?)actorId : null,
                ManagerId = actorType == "MANAGER" ? (long?)actorId : null,
                DoctorId = a.DoctorId,
                ClinicId = a.ClinicId,
                PatientId = a.ChildId,
                ActionCode = CodePrefix + action,
                Description = description,
                Notes = envelope.ToString(Formatting.None),
                ActionDate = DateTime.UtcNow
            });
        }

        // Who a request came from, from the ids the endpoint already receives: a PA id wins, then a
        // manager id, otherwise the doctor.
        public static (string Type, long Id) Actor(long? paId, long? managerId, long doctorId)
        {
            if (paId.HasValue && paId.Value > 0) return ("PA", paId.Value);
            if (managerId.HasValue && managerId.Value > 0) return ("MANAGER", managerId.Value);
            return ("DOCTOR", doctorId);
        }

        // Same, from a verified caller's user id (endpoints that only carry callerUserId).
        public static (string Type, long Id) ActorFromUser(Context db, long? userId, long doctorId)
        {
            if (!userId.HasValue) return ("DOCTOR", doctorId);
            var pa = db.PersonalAssistant.Where(p => p.UserId == userId.Value).Select(p => p.Id).FirstOrDefault();
            if (pa > 0) return ("PA", pa);
            var mgr = db.Manager.Where(m => m.UserId == userId.Value).Select(m => m.Id).FirstOrDefault();
            if (mgr > 0) return ("MANAGER", mgr);
            return ("DOCTOR", doctorId);
        }

        // One entry per open assignment that pins any of these doses ("Penta-1, PCV-1 given").
        // Doses no open assignment pins are not assignment events and are skipped. Caller saves.
        public static void AddForDoses(Context db, string action, IEnumerable<Schedule> doses, string verb,
                                       string actorType, long actorId, object? extra = null)
        {
            var byAssignment = new Dictionary<long, (PAAssignment A, List<Schedule> Doses)>();
            foreach (var s in doses)
            {
                var a = AssignmentResolver.OpenAssignmentForDose(db, s.Id);
                if (a == null) continue;
                if (!byAssignment.ContainsKey(a.Id)) byAssignment[a.Id] = (a, new List<Schedule>());
                byAssignment[a.Id].Doses.Add(s);
            }
            foreach (var g in byAssignment.Values)
            {
                var names = g.Doses.Select(s => DoseName(db, s)).ToList();
                Add(db, action, g.A, actorType, actorType == "DOCTOR" ? g.A.DoctorId : actorId, string.Join(", ", names) + " " + verb, null,
                    new { scheduleIds = g.Doses.Select(s => s.Id).ToList(), extra });
            }
        }

        private static string DoseName(Context db, Schedule s)
        {
            if (s.Dose != null) return s.Dose.Name;
            return db.Doses.Where(d => d.Id == s.DoseId).Select(d => d.Name).FirstOrDefault() ?? "Dose";
        }

        public class Entry
        {
            public DateTime At { get; set; }
            public string Actor { get; set; } = "";
            public long ActorId { get; set; }
            public string ActorName { get; set; } = "";
            public string Action { get; set; } = "";
            public string Text { get; set; } = "";
            public string? Reason { get; set; }
            public string Source { get; set; } = "log";   // "log" = written at the time, "derived" = rebuilt from record timestamps
            public List<long> ScheduleIds { get; set; } = new List<long>();
            public long? InvoiceId { get; set; }
            public decimal? Amount { get; set; }
            public string? Mode { get; set; }
        }

        // Per-dose events of one visit (two doses given, two payments recorded) arrive as one row
        // per dose. Fold rows of the same action by the same person within 10 minutes into one
        // line, as the history reads them: "Penta-1, PCV-1 given".
        private static readonly string[] PerDoseActions = { "Given", "Ungiven", "PaymentRecorded", "PaymentVerified" };

        public static List<Entry> FoldPerDose(List<Entry> ordered, IDictionary<long, string> doseNames)
        {
            var result = new List<Entry>();
            foreach (var e in ordered)
            {
                var last = result.Count > 0 ? result[result.Count - 1] : null;
                if (last != null && e.Source == "log" && last.Source == "log" && PerDoseActions.Contains(e.Action)
                    && last.Action == e.Action && last.Actor == e.Actor && last.ActorId == e.ActorId
                    && last.Mode == e.Mode && (e.At - last.At).TotalMinutes <= 10
                    && e.ScheduleIds.Count > 0 && last.ScheduleIds.Count > 0)
                {
                    last.ScheduleIds = last.ScheduleIds.Union(e.ScheduleIds).ToList();
                    if (e.Amount.HasValue) last.Amount = (last.Amount ?? 0) + e.Amount.Value;
                    var names = string.Join(", ", last.ScheduleIds.Select(id => doseNames.TryGetValue(id, out var n) ? n : "Dose"));
                    last.Text = e.Action == "PaymentRecorded"
                        ? names + " payment recorded: " + (last.Mode ?? "") + (last.Amount > 0 ? ", " + last.Amount.Value.ToString("0") : "")
                        : names + " " + (e.Action == "Given" ? "given" : e.Action == "Ungiven" ? "ungiven" : "payment verified");
                    continue;
                }
                result.Add(e);
            }
            return result;
        }

        // Audit rows for these assignment ids (an assignment and the ones it was reassigned from).
        public static List<Entry> Read(Context db, long childId, ICollection<long> assignmentIds)
        {
            var rows = db.PaActivityLogs
                .Where(l => l.PatientId == childId && l.ActionCode.StartsWith(CodePrefix))
                .OrderBy(l => l.ActionDate)
                .ToList();
            var list = new List<Entry>();
            foreach (var r in rows)
            {
                JObject? env;
                try { env = JObject.Parse(r.Notes ?? "{}"); } catch { continue; }
                var aid = env.Value<long?>("assignmentId");
                if (!aid.HasValue || !assignmentIds.Contains(aid.Value)) continue;
                var data = env["data"] as JObject;
                list.Add(new Entry
                {
                    At = r.ActionDate,
                    Actor = env.Value<string>("actor") ?? "",
                    ActorId = env.Value<long?>("actorId") ?? 0,
                    Action = r.ActionCode.Substring(CodePrefix.Length),
                    Text = r.Description,
                    Reason = env.Value<string>("reason"),
                    Source = "log",
                    ScheduleIds = data?["scheduleIds"] is JArray ids ? ids.Select(x => (long)x).ToList() : new List<long>(),
                    InvoiceId = data?.Value<long?>("invoiceId"),
                    Amount = (data?["extra"] as JObject)?.Value<decimal?>("amount") ?? data?.Value<decimal?>("amount"),
                    Mode = (data?["extra"] as JObject)?.Value<string>("mode")
                });
            }
            return list;
        }
    }
}
