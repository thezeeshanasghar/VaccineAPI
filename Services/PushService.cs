using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using Google.Apis.Auth.OAuth2;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VaccineAPI.Models;

namespace VaccineAPI.Services
{
    // Outcome of one send to one recipient. Result: sent | partial | no_tokens | disabled | fcm_error
    public class PushResult
    {
        public string Result { get; set; } = "";
        public int Tokens { get; set; }
        public int Sent { get; set; }
        public int Removed { get; set; }
        public string Detail { get; set; } = "";
    }

    // Sends FCM (HTTP v1) pushes. Configure with appsettings "Push": { "ServiceAccountPath": "<firebase service-account json>" }
    // (or env PUSH_SERVICE_ACCOUNT). Without it every call is a logged no-op, so the API runs fine before Firebase exists.
    // Every attempt, including skipped ones, is written to pushattempt.
    public static class PushService
    {
        public const string AndroidChannel = "vpk_alerts";

        public static IServiceScopeFactory? Scopes;
        private static string? _projectId;
        private static GoogleCredential? _cred;
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        private static DateTime _lastDisabledWarn = DateTime.MinValue;

        public static void Configure(IConfiguration config, IServiceScopeFactory scopes)
        {
            Scopes = scopes;
            var path = config["Push:ServiceAccountPath"] ?? Environment.GetEnvironmentVariable("PUSH_SERVICE_ACCOUNT");
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                Console.Error.WriteLine("[PUSH] No service account configured (Push:ServiceAccountPath / PUSH_SERVICE_ACCOUNT); pushes are DISABLED.");
                return;
            }
            try
            {
                var json = File.ReadAllText(path);
                _projectId = JObject.Parse(json).Value<string>("project_id");
                _cred = GoogleCredential.FromJson(json).CreateScoped("https://www.googleapis.com/auth/firebase.messaging");
                Console.WriteLine("[PUSH] Enabled for Firebase project " + _projectId);
            }
            catch (Exception e) { Console.Error.WriteLine("[PUSH] Could not load service account: " + e.Message); }
        }

        public static bool Enabled => _cred != null && !string.IsNullOrEmpty(_projectId);

        // Fire-and-forget. Never throws into the caller's request.
        public static void Send(string recipientType, long recipientId, string title, string body, IDictionary<string, string>? data = null)
        {
            if (Scopes == null) return;
            _ = Task.Run(async () =>
            {
                try { await SendAsync(recipientType, recipientId, title, body, data); }
                catch (Exception e) { Console.Error.WriteLine($"[PUSH] send to {recipientType}:{recipientId} crashed: {e.Message}"); }
            });
        }

        // FCM says the token itself is dead only with UNREGISTERED, or INVALID_ARGUMENT pointing at message.token.
        // Any other 4xx/5xx (bad payload, wrong project, quota, outage) must never delete a token.
        public static bool IsDeadToken(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody)) return false;
            try
            {
                var err = JObject.Parse(responseBody)["error"] as JObject;
                if (err == null) return false;
                var details = err["details"] as JArray;
                if (details != null)
                    foreach (var d in details.OfType<JObject>())
                    {
                        if (string.Equals((string?)d["errorCode"], "UNREGISTERED", StringComparison.OrdinalIgnoreCase)) return true;
                        if (d["fieldViolations"] is JArray fv &&
                            fv.OfType<JObject>().Any(v => ((string?)v["field"] ?? "").EndsWith("token", StringComparison.OrdinalIgnoreCase)))
                            return true;
                    }
                return false;
            }
            catch (JsonException) { return false; }
        }

        public static async Task<PushResult> SendAsync(string recipientType, long recipientId, string title, string body,
            IDictionary<string, string>? data, string? dedupKey = null)
        {
            var type = data != null && data.TryGetValue("type", out var t) ? t : "";
            var res = new PushResult();
            using var scope = Scopes!.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Context>();
            try
            {
                if (!Enabled)
                {
                    res.Result = "disabled";
                    res.Detail = "No Firebase service account configured";
                    if (DateTime.UtcNow - _lastDisabledWarn > TimeSpan.FromHours(1))
                    {
                        _lastDisabledWarn = DateTime.UtcNow;
                        Console.Error.WriteLine("[PUSH] Skipping pushes: service account not configured.");
                    }
                    return res;
                }

                var tokens = await db.DeviceTokens.Where(x => x.RecipientType == recipientType && x.RecipientId == recipientId).ToListAsync();
                res.Tokens = tokens.Count;
                if (tokens.Count == 0)
                {
                    res.Result = "no_tokens";
                    res.Detail = "Recipient has no registered device";
                    return res;
                }

                string access;
                try { access = await _cred!.UnderlyingCredential.GetAccessTokenForRequestAsync(); }
                catch (Exception e)
                {
                    res.Result = "fcm_error";
                    res.Detail = "Could not get FCM access token: " + e.Message;
                    return res;
                }

                var dead = new List<DeviceToken>();
                var errors = new List<string>();
                foreach (var tok in tokens)
                {
                    try
                    {
                        var msg = new JObject
                        {
                            ["message"] = new JObject
                            {
                                ["token"] = tok.Token,
                                ["notification"] = new JObject { ["title"] = title, ["body"] = body },
                                ["data"] = JObject.FromObject(data ?? new Dictionary<string, string>()),
                                ["android"] = new JObject
                                {
                                    ["priority"] = "HIGH",
                                    ["notification"] = new JObject { ["channel_id"] = AndroidChannel }
                                }
                            }
                        };
                        using var req = new HttpRequestMessage(HttpMethod.Post, $"https://fcm.googleapis.com/v1/projects/{_projectId}/messages:send")
                        {
                            Content = new StringContent(msg.ToString(Formatting.None), Encoding.UTF8, "application/json")
                        };
                        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
                        using var http = await Http.SendAsync(req);
                        if (http.IsSuccessStatusCode) { res.Sent++; continue; }
                        var text = await http.Content.ReadAsStringAsync();
                        if (IsDeadToken(text)) { dead.Add(tok); errors.Add($"token {tok.Id} dead"); }
                        else errors.Add($"token {tok.Id} FCM {(int)http.StatusCode}: {Trim(text, 200)}");
                    }
                    catch (Exception e) { errors.Add($"token {tok.Id}: {e.Message}"); }
                }

                if (dead.Count > 0)
                {
                    try
                    {
                        db.DeviceTokens.RemoveRange(dead);
                        await db.SaveChangesAsync();
                        res.Removed = dead.Count;
                    }
                    catch (Exception e) { errors.Add("dead-token cleanup failed: " + e.Message); }
                }
                res.Result = res.Sent == 0 ? "fcm_error" : (res.Sent < res.Tokens ? "partial" : "sent");
                res.Detail = string.Join("; ", errors);
                return res;
            }
            catch (Exception e)
            {
                res.Result = "fcm_error";
                res.Detail = e.Message;
                return res;
            }
            finally
            {
                if (res.Result != "") await LogAttempt(db, recipientType, recipientId, type, title, res, dedupKey);
            }
        }

        private static string Trim(string s, int n) => s.Length <= n ? s : s.Substring(0, n);

        private static async Task LogAttempt(Context db, string recipientType, long recipientId, string type, string title, PushResult r, string? dedupKey)
        {
            try
            {
                db.PushAttempts.Add(new PushAttempt
                {
                    RecipientType = recipientType, RecipientId = recipientId, Type = Trim(type, 64), Title = Trim(title, 200),
                    Result = r.Result, TokenCount = r.Tokens, SentCount = r.Sent, RemovedCount = r.Removed,
                    Detail = Trim(r.Detail, 1000), DedupKey = dedupKey, CreatedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }
            catch (Exception e) { Console.Error.WriteLine("[PUSH] could not write pushattempt: " + e.Message); }
        }

        // Who has at least one device, cached for a minute so the 5-minute scheduler does not query per candidate.
        private static HashSet<(string, long)> _owners = new HashSet<(string, long)>();
        private static DateTime _ownersAt = DateTime.MinValue;

        private static async Task<bool> HasToken(Context db, string type, long id)
        {
            if (DateTime.UtcNow - _ownersAt > TimeSpan.FromSeconds(60))
            {
                var rows = await db.DeviceTokens.Select(x => new { x.RecipientType, x.RecipientId }).Distinct().ToListAsync();
                _owners = rows.Select(r => (r.RecipientType, r.RecipientId)).ToHashSet();
                _ownersAt = DateTime.UtcNow;
            }
            return _owners.Contains((type, id));
        }

        // A skipped scheduled push is logged once per key per process, not on every 5-minute tick.
        // Parents are not logged here: most never installed the app and it would drown the table.
        private static readonly ConcurrentDictionary<string, DateTime> SkipLogged = new ConcurrentDictionary<string, DateTime>();

        private static async Task LogSkipOnce(Context db, string key, string type, long id, string title, string result, string detail)
        {
            if (type == "PARENT") return;
            if (SkipLogged.Count > 5000)
                foreach (var k in SkipLogged.Where(x => x.Value < DateTime.UtcNow.AddDays(-2)).Select(x => x.Key).ToList()) SkipLogged.TryRemove(k, out _);
            if (!SkipLogged.TryAdd(key, DateTime.UtcNow)) return;
            await LogAttempt(db, type, id, key.Split(':')[0], title, new PushResult { Result = result, Detail = detail }, key);
        }

        // Removes claimed keys again so a failed send is retried on the next tick.
        public static async Task ReleaseKeys(Context db, IEnumerable<string> keys)
        {
            var list = keys.ToList();
            try { await db.PushLogs.Where(p => list.Contains(p.DedupKey)).ExecuteDeleteAsync(); }
            catch (Exception e) { Console.Error.WriteLine("[PUSH] could not release dedup keys: " + e.Message); }
        }

        // Scheduler send: claims the key, sends, and gives the key back if nothing was delivered so the next tick retries.
        // Recipients with no device are skipped before claiming. Returns true only when at least one device got it.
        public static async Task<bool> SendOnceAsync(Context db, string dedupKey, string recipientType, long recipientId, string title, string body, IDictionary<string, string>? data = null)
            => await SendOnceAsync(db, new[] { dedupKey }, recipientType, recipientId, title, body, data);

        public static async Task<bool> SendOnceAsync(Context db, IEnumerable<string> keys, string recipientType, long recipientId, string title, string body, IDictionary<string, string>? data = null)
        {
            var list = keys.ToList();
            if (list.Count == 0) return false;
            if (!Enabled) return false;
            if (!await HasToken(db, recipientType, recipientId))
            {
                await LogSkipOnce(db, list[0], recipientType, recipientId, title, "no_tokens", "Recipient has no registered device");
                return false;
            }
            var existing = await db.PushLogs.Where(p => list.Contains(p.DedupKey)).Select(p => p.DedupKey).ToListAsync();
            var fresh = list.Except(existing).ToList();
            if (fresh.Count == 0) return false;
            foreach (var k in fresh) db.PushLogs.Add(new PushLog { DedupKey = k, SentAt = DateTime.UtcNow });
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException) { db.ChangeTracker.Clear(); return false; } // lost a race with another instance
            var r = await SendAsync(recipientType, recipientId, title, body, data, fresh[0]);
            if (r.Sent > 0) return true;
            await ReleaseKeys(db, fresh);
            return false;
        }
    }
}
