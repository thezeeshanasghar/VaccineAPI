using System.Net.Http.Headers;
using System.Text;
using Google.Apis.Auth.OAuth2;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VaccineAPI.Models;

namespace VaccineAPI.Services
{
    // Sends FCM (HTTP v1) pushes. Configure with appsettings "Push": { "ServiceAccountPath": "<firebase service-account json>" }
    // (or env PUSH_SERVICE_ACCOUNT). Without it every call is a logged no-op, so the API runs fine before Firebase exists.
    public static class PushService
    {
        public static IServiceScopeFactory? Scopes;
        private static string? _projectId;
        private static GoogleCredential? _cred;
        private static bool _initTried;
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        public static void Configure(IConfiguration config, IServiceScopeFactory scopes)
        {
            Scopes = scopes;
            _initTried = true;
            var path = config["Push:ServiceAccountPath"] ?? Environment.GetEnvironmentVariable("PUSH_SERVICE_ACCOUNT");
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                Console.WriteLine("[PUSH] No service account configured; pushes are disabled.");
                return;
            }
            try
            {
                var json = File.ReadAllText(path);
                _projectId = JObject.Parse(json).Value<string>("project_id");
                _cred = GoogleCredential.FromJson(json).CreateScoped("https://www.googleapis.com/auth/firebase.messaging");
            }
            catch (Exception e) { Console.Error.WriteLine("[PUSH] Could not load service account: " + e.Message); }
        }

        public static bool Enabled => _cred != null && !string.IsNullOrEmpty(_projectId);

        // Fire-and-forget. Never throws into the caller's request.
        public static void Send(string recipientType, long recipientId, string title, string body, IDictionary<string, string>? data = null)
        {
            if (!Enabled || Scopes == null) return;
            _ = Task.Run(async () =>
            {
                try { await SendAsync(recipientType, recipientId, title, body, data); }
                catch (Exception e) { Console.Error.WriteLine("[PUSH] send failed: " + e.Message); }
            });
        }

        public static async Task SendAsync(string recipientType, long recipientId, string title, string body, IDictionary<string, string>? data)
        {
            if (!Enabled || Scopes == null) return;
            using var scope = Scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Context>();
            var tokens = await db.DeviceTokens.Where(t => t.RecipientType == recipientType && t.RecipientId == recipientId).ToListAsync();
            if (tokens.Count == 0) return;

            var access = await _cred!.UnderlyingCredential.GetAccessTokenForRequestAsync();
            var dead = new List<DeviceToken>();
            foreach (var t in tokens)
            {
                var msg = new JObject
                {
                    ["message"] = new JObject
                    {
                        ["token"] = t.Token,
                        ["notification"] = new JObject { ["title"] = title, ["body"] = body },
                        ["data"] = JObject.FromObject(data ?? new Dictionary<string, string>()),
                        ["android"] = new JObject { ["priority"] = "HIGH" }
                    }
                };
                var req = new HttpRequestMessage(HttpMethod.Post, $"https://fcm.googleapis.com/v1/projects/{_projectId}/messages:send")
                {
                    Content = new StringContent(msg.ToString(Formatting.None), Encoding.UTF8, "application/json")
                };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
                var res = await Http.SendAsync(req);
                if (res.IsSuccessStatusCode) continue;
                var text = await res.Content.ReadAsStringAsync();
                if ((int)res.StatusCode == 404 || text.Contains("UNREGISTERED") || text.Contains("INVALID_ARGUMENT"))
                    dead.Add(t);
                else
                    Console.Error.WriteLine($"[PUSH] FCM {(int)res.StatusCode}: {text}");
            }
            if (dead.Count > 0)
            {
                db.DeviceTokens.RemoveRange(dead);
                await db.SaveChangesAsync();
            }
        }

        // Pushes whose DedupKey has not been used yet. Returns false (and sends nothing) if already sent.
        public static async Task<bool> SendOnceAsync(Context db, string dedupKey, string recipientType, long recipientId, string title, string body, IDictionary<string, string>? data = null)
        {
            if (await db.PushLogs.AnyAsync(p => p.DedupKey == dedupKey)) return false;
            db.PushLogs.Add(new PushLog { DedupKey = dedupKey, SentAt = DateTime.UtcNow });
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException) { return false; } // lost a race with another instance
            Send(recipientType, recipientId, title, body, data);
            return true;
        }
    }
}
