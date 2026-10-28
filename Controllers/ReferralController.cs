using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;
using VaccineAPI.ModelDTO;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace VaccineAPI.Controllers
{
    // Two-step agent registration (AGENT_REFERRAL_TWO_STEP_SPEC.md): an agent files a referral
    // (basic details only, no Child row); clinic staff later register the patient through the normal
    // Add Patient form and then call convert here to close the referral. Staff scope follows the
    // rest of the API: anyone under the clinic's doctor (CallerGuard.OwnsClinic).
    [Route("api/[controller]")]
    [ApiController]
    public class ReferralController : ControllerBase
    {
        private const string DuplicateReason = "Duplicate — first referrer applies";
        private static readonly string[] DobFormats = { "dd-MM-yyyy", "yyyy-MM-dd" };

        private readonly Context _db;

        public ReferralController(Context db)
        {
            _db = db;
        }

        // ---------------------------------------------------------------- agent side

        // POST: api/Referral — an agent files a referral into their default clinic's queue.
        [RolesOnly("AGENT", "SUPERADMIN")]
        [Owns(OwnerKind.Agent, "AgentId")]
        [HttpPost]
        public Response<object> PostReferral(AgentReferralCreateDTO dto)
        {
            if (dto == null) return Fail("Referral details are required.");

            var name = (dto.Name ?? "").Trim();
            var guardian = (dto.FatherName ?? "").Trim();
            var gender = (dto.Gender ?? "").Trim();
            if (name.Length == 0 || guardian.Length == 0)
                return Fail("Patient name and guardian name are required.");
            if (gender != "Boy" && gender != "Girl")
                return Fail("Gender is required.");

            if (!DateTime.TryParseExact((dto.DOB ?? "").Trim(), DobFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dob))
                return Fail("Date of birth is not valid.");
            dob = dob.Date;
            if (dob > Pkt.Today())
                return Fail("Date of birth cannot be in the future.");

            var phoneErr = PhoneRules.Check(dto.CountryCode, dto.MobileNumber, out var phoneNorm);
            if (phoneErr != null) return Fail(phoneErr);

            var email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim();
            if (email != null && (email.Length > 255 || !email.Contains("@")))
                return Fail("Email is not valid.");
            var note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
            if (note != null && note.Length > 200) note = note.Substring(0, 200);

            var agent = _db.Agents.FirstOrDefault(a => a.Id == dto.AgentId);
            if (agent == null) return Fail("Agent not found.");
            // Until the CanRegister* checkboxes are replaced by a single "can refer" permission, any of
            // them on means the agent may refer. Agents with none stay verification-only, as before.
            if (!(agent.CanRegisterRegular || agent.CanRegisterEPI || agent.CanRegisterCustomize || agent.CanRegisterTravel))
                return Fail("You are not currently permitted to refer patients. Ask your doctor to enable this.");
            // Clinic always comes from the agent's stored default, never from the client.
            if (!agent.ClinicId.HasValue || agent.ClinicId.Value <= 0)
                return Fail("No clinic is assigned to you. Ask the doctor to assign your clinic before referring.");

            var clinic = _db.Clinics.FirstOrDefault(c => c.Id == agent.ClinicId.Value);
            if (clinic == null) return Fail("Your assigned clinic was not found. Ask the doctor to check it.");

            var nextDay = dob.AddDays(1);

            // Already registered at this doctor's practice: say so, create nothing, reveal nothing.
            var doctorClinicIds = _db.Clinics.Where(c => c.DoctorId == clinic.DoctorId).Select(c => c.Id).ToList();
            bool alreadyRegistered = _db.Childs.Any(c => doctorClinicIds.Contains(c.ClinicId)
                && c.User != null && c.User.MobileNumber == phoneNorm
                && c.DOB >= dob && c.DOB < nextDay);
            if (alreadyRegistered)
                return Fail("This patient is already registered at the clinic. No referral was created.");

            // The same agent re-sending an open referral is a plain duplicate.
            bool sameAgentOpen = _db.AgentReferrals.Any(r => r.AgentId == agent.Id
                && r.Status == AgentReferral.StatusNew
                && r.MobileNumber == phoneNorm && r.DOB == dob);
            if (sameAgentOpen)
                return Fail("You already referred this patient. Check My Patients for its status.");

            var referral = new AgentReferral
            {
                AgentId = agent.Id,
                ClinicId = clinic.Id,
                Name = name.ToUpper(),
                FatherName = guardian.ToUpper(),
                Gender = gender,
                DOB = dob,
                CountryCode = (dto.CountryCode ?? "92").Trim(),
                MobileNumber = phoneNorm,
                City = string.IsNullOrWhiteSpace(dto.City) ? null : dto.City.Trim(),
                Email = email,
                Note = note,
                Status = AgentReferral.StatusNew,
                CreatedAt = DateTime.UtcNow
            };
            _db.AgentReferrals.Add(referral);
            _db.SaveChanges();

            // Notify the clinic's doctor. A notification failure must never fail the referral.
            try
            {
                _db.Notifications.Add(new Notification
                {
                    Type = "AgentReferral",
                    RecipientType = "DOCTOR",
                    RecipientId = clinic.DoctorId,
                    ClinicId = clinic.Id,
                    Title = "New Patient Referral",
                    Message = agent.Name + " referred " + referral.Name + " to " + clinic.Name + ". Open Referrals to register.",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
                _db.SaveChanges();
            }
            catch (Exception e)
            {
                Console.WriteLine("Referral notification failed: " + e);
            }

            return new Response<object>(true, "Referral sent.", new { referral.Id });
        }

        // GET: api/Referral/agent/{agentId}/patients?query= — the agent's merged "My Patients" list:
        // every referral they filed plus any patient already registered under them that did not come
        // through a referral. A converted referral is shown once, as its referral row.
        [RolesOnly("AGENT", "SUPERADMIN")]
        [Owns(OwnerKind.Agent, "agentId")]
        [HttpGet("agent/{agentId}/patients")]
        public Response<object> GetAgentPatients(int agentId, [FromQuery] string? query)
        {
            var referralsQ = _db.AgentReferrals.Where(r => r.AgentId == agentId);
            var q = string.IsNullOrWhiteSpace(query) ? null : query.Trim().ToLower();
            if (q != null)
                referralsQ = referralsQ.Where(r => r.Name.ToLower().Contains(q)
                    || r.FatherName.ToLower().Contains(q) || r.MobileNumber.Contains(q));

            var referrals = referralsQ.OrderByDescending(r => r.CreatedAt).Take(200).ToList();
            var linkedChildIds = _db.AgentReferrals
                .Where(r => r.AgentId == agentId && r.ChildId.HasValue)
                .Select(r => r.ChildId.Value).ToList();

            var childrenQ = _db.Childs.Include(c => c.User).Where(c => c.AgentId == agentId && !linkedChildIds.Contains(c.Id));
            if (q != null)
                childrenQ = childrenQ.Where(c => (c.Name != null && c.Name.ToLower().Contains(q))
                    || (c.FatherName != null && c.FatherName.ToLower().Contains(q))
                    || (c.User != null && c.User.MobileNumber != null && c.User.MobileNumber.Contains(q)));
            var children = childrenQ.OrderByDescending(c => c.Id).Take(200).ToList();

            // "Billed" = the same condition the referral fee report uses for the first eligible dose.
            var childIds = referrals.Where(r => r.ChildId.HasValue).Select(r => r.ChildId.Value)
                .Concat(children.Select(c => c.Id)).Distinct().ToList();
            var billed = new HashSet<long>(_db.Schedules
                .Where(s => childIds.Contains(s.ChildId) && s.IsDone && s.InvoiceSubmissionId != null)
                .Select(s => s.ChildId).Distinct().ToList());

            var rows = new List<(int Rank, DateTime? Date, object Row)>();
            foreach (var r in referrals)
            {
                string stage = r.Status == AgentReferral.StatusNew ? "Referred"
                    : r.Status == AgentReferral.StatusRejected ? "Not registered"
                    : (r.ChildId.HasValue && billed.Contains(r.ChildId.Value) ? "Billed" : "Registered");
                rows.Add((stage == "Referred" ? 0 : 1, r.CreatedAt, new
                {
                    Source = "referral",
                    r.Id,
                    r.ChildId,
                    r.Name,
                    Guardian = r.FatherName,
                    r.MobileNumber,
                    Date = Pkt.Format(r.CreatedAt, "yyyy-MM-dd"),
                    Stage = stage,
                    Reason = r.Status == AgentReferral.StatusRejected ? PlainReason(r.RejectReason) : null
                }));
            }
            foreach (var c in children)
            {
                rows.Add((1, null, new
                {
                    Source = "child",
                    c.Id,
                    ChildId = (long?)c.Id,
                    c.Name,
                    Guardian = c.FatherName,
                    MobileNumber = c.User != null ? c.User.MobileNumber : null,
                    Date = (string?)null,
                    Stage = billed.Contains(c.Id) ? "Billed" : "Registered",
                    Reason = (string?)null
                }));
            }

            var ordered = rows.OrderBy(x => x.Rank).ThenByDescending(x => x.Date ?? DateTime.MinValue).Select(x => x.Row).ToList();
            return new Response<object>(true, "", ordered);
        }

        // ---------------------------------------------------------------- clinic staff side

        // GET: api/Referral/doctor/{doctorId}?status=New|Done — the Referrals queue for every clinic of
        // the doctor (a referral lands in the agent's default clinic, which need not be the one staff
        // have open). New: oldest first. Done (Converted + Rejected): latest 100, newest first.
        // Also returns the doctor's clinics, for the "send to another clinic" choice.
        [RolesOnly("DOCTOR", "PA", "MANAGER", "SUPERADMIN")]
        [Owns(OwnerKind.Doctor, "doctorId")]
        [HttpGet("doctor/{doctorId}")]
        public Response<object> GetDoctorReferrals(long doctorId, [FromQuery] string? status)
        {
            bool done = string.Equals(status, "Done", StringComparison.OrdinalIgnoreCase);
            var clinics = _db.Clinics.Where(c => c.DoctorId == doctorId).Select(c => new { c.Id, c.Name }).ToList();
            var clinicIds = clinics.Select(c => c.Id).ToList();

            var q = _db.AgentReferrals.Where(r => clinicIds.Contains(r.ClinicId));
            q = done
                ? q.Where(r => r.Status != AgentReferral.StatusNew).OrderByDescending(r => r.ResolvedAt).Take(100)
                : q.Where(r => r.Status == AgentReferral.StatusNew).OrderBy(r => r.CreatedAt);
            var list = q.ToList();

            var agentIds = list.Select(r => r.AgentId).Distinct().ToList();
            var agents = _db.Agents.Where(a => agentIds.Contains(a.Id)).ToDictionary(a => a.Id);
            var clinicNames = clinics.ToDictionary(c => c.Id, c => c.Name);

            // Open referrals for the same person from more than one agent: the earliest wins on convert.
            var firstByKey = list.Where(r => r.Status == AgentReferral.StatusNew)
                .GroupBy(r => r.MobileNumber + "|" + r.DOB.ToString("yyyyMMdd"))
                .ToDictionary(g => g.Key, g => new { First = g.OrderBy(x => x.CreatedAt).First().Id, Count = g.Count() });

            var rows = list.Select(r =>
            {
                string key = r.MobileNumber + "|" + r.DOB.ToString("yyyyMMdd");
                bool dup = r.Status == AgentReferral.StatusNew && firstByKey.ContainsKey(key) && firstByKey[key].Count > 1;
                return new
                {
                    r.Id,
                    r.AgentId,
                    AgentName = agents.ContainsKey(r.AgentId) ? agents[r.AgentId].Name : "",
                    AgentCode = agents.ContainsKey(r.AgentId) ? agents[r.AgentId].AgentCode : "",
                    r.ClinicId,
                    ClinicName = clinicNames.ContainsKey(r.ClinicId) ? clinicNames[r.ClinicId] : "",
                    r.Name,
                    r.FatherName,
                    r.Gender,
                    DOB = r.DOB.ToString("yyyy-MM-dd"),
                    r.CountryCode,
                    r.MobileNumber,
                    r.City,
                    r.Email,
                    r.Note,
                    r.Status,
                    r.ChildId,
                    r.RejectReason,
                    CreatedAt = DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc),
                    r.ResolvedAt,
                    IsDuplicate = dup,
                    IsFirstReferrer = dup && firstByKey[key].First == r.Id
                };
            }).ToList();

            return new Response<object>(true, "", new { Items = rows, Clinics = clinics });
        }

        // GET: api/Referral/match?doctorId=&countryCode=&mobile=&dob= — used by Add Patient: does an open referral
        // match the phone + DOB being typed? Returns the earliest (first referrer wins), or null.
        [RolesOnly("DOCTOR", "PA", "MANAGER", "SUPERADMIN")]
        [Owns(OwnerKind.Doctor, "doctorId")]
        [HttpGet("match")]
        public Response<object> Match([FromQuery] long doctorId, [FromQuery] string countryCode, [FromQuery] string mobile, [FromQuery] string dob)
        {
            if (PhoneRules.Check(countryCode ?? "92", mobile ?? "", out var norm) != null
                || !DateTime.TryParseExact((dob ?? "").Trim(), DobFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return new Response<object>(true, "", null);

            var clinicIds = _db.Clinics.Where(c => c.DoctorId == doctorId).Select(c => c.Id).ToList();

            var r = _db.AgentReferrals
                .Where(x => clinicIds.Contains(x.ClinicId) && x.Status == AgentReferral.StatusNew
                    && x.MobileNumber == norm && x.DOB == d.Date)
                .OrderBy(x => x.CreatedAt).FirstOrDefault();
            if (r == null) return new Response<object>(true, "", null);

            var agent = _db.Agents.FirstOrDefault(a => a.Id == r.AgentId);
            return new Response<object>(true, "", new
            {
                r.Id,
                r.AgentId,
                AgentName = agent != null ? agent.Name : "",
                AgentCode = agent != null ? agent.AgentCode : "",
                r.Name,
                r.FatherName,
                r.Gender,
                DOB = r.DOB.ToString("yyyy-MM-dd"),
                r.CountryCode,
                r.MobileNumber,
                r.City,
                r.Email,
                r.Note,
                CreatedAt = DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc)
            });
        }

        // GET: api/Referral/{id} — one referral, to prefill Add Patient from the queue.
        [RolesOnly("DOCTOR", "PA", "MANAGER", "SUPERADMIN")]
        [HttpGet("{id}")]
        public Response<object> GetOne(long id)
        {
            var r = _db.AgentReferrals.FirstOrDefault(x => x.Id == id);
            if (r == null) return Fail("Referral not found.");
            if (!CallerGuard.OwnsClinic(_db, r.ClinicId)) return Fail("This referral belongs to another practice.");
            var agent = _db.Agents.FirstOrDefault(a => a.Id == r.AgentId);
            var refClinic = _db.Clinics.FirstOrDefault(c => c.Id == r.ClinicId);
            return new Response<object>(true, "", new
            {
                r.Id,
                r.AgentId,
                AgentName = agent != null ? agent.Name : "",
                AgentCode = agent != null ? agent.AgentCode : "",
                r.ClinicId,
                ClinicName = refClinic != null ? refClinic.Name : "",
                r.Name,
                r.FatherName,
                r.Gender,
                DOB = r.DOB.ToString("yyyy-MM-dd"),
                r.CountryCode,
                r.MobileNumber,
                r.City,
                r.Email,
                r.Note,
                r.Status,
                r.ChildId
            });
        }

        // PUT: api/Referral/{id}/convert — called by VacDoc AFTER the normal Add Patient save worked.
        // Idempotent. First referrer wins: if more than one agent has an open referral for this
        // phone + DOB at this practice, the earliest is the one converted and credited, and the
        // others are closed as duplicates. Child.AgentId is set to the winner unless the child already
        // belongs to a different agent through some other path.
        [RolesOnly("DOCTOR", "PA", "MANAGER", "SUPERADMIN")]
        [HttpPut("{id}/convert")]
        public Response<object> Convert(long id, [FromBody] ReferralConvertDTO dto)
        {
            var r = _db.AgentReferrals.FirstOrDefault(x => x.Id == id);
            if (r == null) return Fail("Referral not found.");
            if (!CallerGuard.OwnsClinic(_db, r.ClinicId)) return Fail("This referral belongs to another practice.");

            if (r.Status == AgentReferral.StatusConverted)
                return new Response<object>(true, "Already converted.", new { ReferralId = r.Id, r.ChildId, r.AgentId });
            if (r.Status == AgentReferral.StatusRejected)
                return Fail("This referral was rejected.");

            if (dto == null || dto.ChildId <= 0) return Fail("ChildId is required.");
            var child = _db.Childs.FirstOrDefault(c => c.Id == dto.ChildId);
            if (child == null) return Fail("Patient not found.");
            if (!CallerGuard.OwnsChild(_db, child.Id)) return Fail("This patient belongs to another practice.");

            var clinic = _db.Clinics.FirstOrDefault(c => c.Id == r.ClinicId);
            var clinicIds = _db.Clinics.Where(c => c.DoctorId == clinic.DoctorId).Select(c => c.Id).ToList();
            var open = _db.AgentReferrals
                .Where(x => clinicIds.Contains(x.ClinicId) && x.Status == AgentReferral.StatusNew
                    && x.MobileNumber == r.MobileNumber && x.DOB == r.DOB)
                .OrderBy(x => x.CreatedAt).ToList();
            var winner = open.First();

            var now = DateTime.UtcNow;
            var (role, resolverId) = Resolver();
            foreach (var x in open)
            {
                x.ResolvedAt = now;
                x.ResolvedByRole = role;
                x.ResolvedById = resolverId;
                if (x.Id == winner.Id)
                {
                    x.Status = AgentReferral.StatusConverted;
                    x.ChildId = child.Id;
                }
                else
                {
                    x.Status = AgentReferral.StatusRejected;
                    x.RejectReason = DuplicateReason;
                }
            }

            // The Add Patient save already stamped the agent from the referral the staff clicked; make
            // sure it is the first referrer, but never take a child away from an unrelated agent.
            if (!child.AgentId.HasValue || child.AgentId.Value == r.AgentId || child.AgentId.Value == winner.AgentId)
                child.AgentId = winner.AgentId;

            _db.SaveChanges();
            return new Response<object>(true, "Referral converted.", new { ReferralId = winner.Id, ChildId = child.Id, winner.AgentId });
        }

        // PUT: api/Referral/{id}/reject — close a referral that will not become a patient.
        [RolesOnly("DOCTOR", "PA", "MANAGER", "SUPERADMIN")]
        [HttpPut("{id}/reject")]
        public Response<object> Reject(long id, [FromBody] ReferralRejectDTO dto)
        {
            var r = _db.AgentReferrals.FirstOrDefault(x => x.Id == id);
            if (r == null) return Fail("Referral not found.");
            if (!CallerGuard.OwnsClinic(_db, r.ClinicId)) return Fail("This referral belongs to another practice.");
            if (r.Status != AgentReferral.StatusNew) return Fail("This referral is already closed.");

            var reason = dto == null ? "" : (dto.Reason ?? "").Trim();
            if (reason.Length == 0) return Fail("A reason is required.");
            if (reason.Length > 200) reason = reason.Substring(0, 200);

            var (role, resolverId) = Resolver();
            r.Status = AgentReferral.StatusRejected;
            r.RejectReason = reason;
            r.ResolvedAt = DateTime.UtcNow;
            r.ResolvedByRole = role;
            r.ResolvedById = resolverId;
            _db.SaveChanges();
            return new Response<object>(true, "Referral rejected.", null);
        }

        // PUT: api/Referral/{id}/clinic — the family goes to another branch of the same practice.
        [RolesOnly("DOCTOR", "PA", "MANAGER", "SUPERADMIN")]
        [HttpPut("{id}/clinic")]
        public Response<object> ChangeClinic(long id, [FromBody] ReferralClinicDTO dto)
        {
            var r = _db.AgentReferrals.FirstOrDefault(x => x.Id == id);
            if (r == null) return Fail("Referral not found.");
            if (!CallerGuard.OwnsClinic(_db, r.ClinicId)) return Fail("This referral belongs to another practice.");
            if (r.Status != AgentReferral.StatusNew) return Fail("This referral is already closed.");

            if (dto == null || dto.ClinicId <= 0) return Fail("Clinic is required.");
            var from = _db.Clinics.FirstOrDefault(c => c.Id == r.ClinicId);
            var to = _db.Clinics.FirstOrDefault(c => c.Id == dto.ClinicId);
            if (to == null || from == null || to.DoctorId != from.DoctorId || !CallerGuard.OwnsClinic(_db, to.Id))
                return Fail("That clinic is not available.");

            r.ClinicId = to.Id;
            _db.SaveChanges();
            return new Response<object>(true, "Referral moved to " + to.Name + ".", null);
        }

        // ---------------------------------------------------------------- helpers

        private static Response<object> Fail(string message) => new Response<object>(false, message, null);

        private static (string Role, long? Id) Resolver()
        {
            var id = AuthContext.Current;
            if (id == null) return ("", null);
            long? who = id.Role == "PA" ? id.PaId
                : id.Role == "MANAGER" ? id.ManagerId
                : id.Role == "DOCTOR" ? id.DoctorId
                : id.UserId;
            return (id.Role, who);
        }

        // What the agent sees for a rejected referral: plain words, never the internal reason code,
        // and never a hint that another agent referred the same patient.
        private static string PlainReason(string? reason)
        {
            var r = (reason ?? "").Trim();
            if (r.Length == 0) return "Not registered.";
            if (r.StartsWith("Duplicate", StringComparison.OrdinalIgnoreCase)) return "This patient was already referred.";
            switch (r)
            {
                case "Did not come": return "Patient did not visit the clinic.";
                case "Wrong number": return "Wrong number. Please resubmit with the correct one.";
                case "Already registered": return "Patient was already registered at the clinic.";
                default: return r.StartsWith("Other:", StringComparison.OrdinalIgnoreCase) ? r.Substring(6).Trim() : r;
            }
        }
    }
}
