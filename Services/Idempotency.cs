using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VaccineAPI.Models;

namespace VaccineAPI.Services
{
    // Duplicate-request protection for the stock-creating endpoints. The client sends the same
    // ClientRequestId when it retries / double-clicks / refreshes; the second request is answered
    // with the stored response of the first and changes nothing.
    public static class Idempotency
    {
        public static IActionResult? TryReplay(Context db, long doctorId, string? clientRequestId, string endpoint)
        {
            if (string.IsNullOrWhiteSpace(clientRequestId)) return null;
            var key = db.IdempotencyKeys.FirstOrDefault(k => k.DoctorId == doctorId && k.Endpoint == endpoint && k.ClientRequestId == clientRequestId);
            if (key == null) return null;
            var body = JObject.Parse(key.ResponseJson);
            body["IsDuplicate"] = true;
            return new OkObjectResult(body);
        }

        // Call inside the request's transaction, right before it commits.
        public static void Record(Context db, long doctorId, string? clientRequestId, string endpoint, object response)
        {
            if (string.IsNullOrWhiteSpace(clientRequestId)) return;
            db.IdempotencyKeys.Add(new IdempotencyKey
            {
                DoctorId = doctorId,
                ClientRequestId = clientRequestId!,
                Endpoint = endpoint,
                ResponseJson = JsonConvert.SerializeObject(response)
            });
        }
    }
}
