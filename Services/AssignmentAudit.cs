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

        public class Entry
        {
            public DateTime At { get; set; }
            public string Actor { get; set; } = "";
            public long ActorId { get; set; }
            public string Action { get; set; } = "";
            public string Text { get; set; } = "";
            public string? Reason { get; set; }
            public string Source { get; set; } = "log";   // "log" = written at the time, "derived" = rebuilt from record timestamps
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
                list.Add(new Entry
                {
                    At = r.ActionDate,
                    Actor = env.Value<string>("actor") ?? "",
                    ActorId = env.Value<long?>("actorId") ?? 0,
                    Action = r.ActionCode.Substring(CodePrefix.Length),
                    Text = r.Description,
                    Reason = env.Value<string>("reason"),
                    Source = "log"
                });
            }
            return list;
        }
    }
}
