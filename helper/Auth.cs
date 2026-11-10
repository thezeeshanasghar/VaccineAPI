using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VaccineAPI.Models;

namespace VaccineAPI
{
    // Who is calling, as proven by a valid session token (never by ids in the request body).
    public sealed class AuthIdentity
    {
        public long UserId { get; init; }          // Users.Id, or Agents.Id when Role == "AGENT"
        public string Role { get; init; } = "";    // DOCTOR | PA | MANAGER | PARENT | SUPERADMIN | AGENT
        public long? DoctorId { get; init; }       // the doctor this account belongs to (own id for a doctor)
        public long? PaId { get; init; }
        public long? ManagerId { get; init; }
    }

    // Static access to the current request's identity and the auth settings, so the shared
    // guard helpers (CallerGuard, StockActionGuard, ...) can use the token without every
    // controller call site having to pass HttpContext.
    public static class AuthContext
    {
        public static IHttpContextAccessor? Accessor;

        // Off     : tokens are not read at all.
        // Log     : tokens are read and used when present; requests without one are logged but served.
        // Enforce : requests without a valid token get 401 (except the public list).
        public static string Mode = "Log";
        public static string Secret = "";

        public static bool Enforcing => string.Equals(Mode, "Enforce", StringComparison.OrdinalIgnoreCase);

        public static AuthIdentity? Current
        {
            get
            {
                var ctx = Accessor?.HttpContext;
                return ctx != null && ctx.Items.TryGetValue("auth.identity", out var v) ? v as AuthIdentity : null;
            }
        }

        // True when the request is provably from this user (token present and matching), or, while the
        // API is still in Log mode, when an older app sent no token at all.
        public static bool CallerIsUser(long userId)
        {
            var id = Current;
            if (id != null) return id.Role != "AGENT" && id.UserId == userId;
            return !Enforcing;
        }

        public static bool IsSuperAdmin => Current?.Role == "SUPERADMIN";

        public static void Configure(IConfiguration config)
        {
            Mode = config["Auth:Mode"] ?? Environment.GetEnvironmentVariable("AuthMode") ?? "Log";

            string? s = config["Auth:TokenSecret"] ?? Environment.GetEnvironmentVariable("AuthTokenSecret");
            if (string.IsNullOrWhiteSpace(s) || s.Length < 32 || s.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
            {
                // Never sign with a known or empty key. A random per-process key keeps the API safe,
                // but every restart signs everyone out until a real secret is configured.
                s = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
                Console.Error.WriteLine("[AUTH] Auth:TokenSecret (or AuthTokenSecret env var) is missing or too short. " +
                                        "Using a random key for this process; users are signed out on every restart. Set a 32+ character secret.");
            }
            Secret = s;
        }

        // Issues the token a login response hands to the app.
        public static string IssueForUser(User u) => AuthToken.Issue('U', u.Id, u.SecurityStamp, Secret);
        public static string IssueForAgent(Agent a) => AuthToken.Issue('A', a.Id, a.Password, Secret);
    }

    // Looks the account up (cached for a minute) so a deactivated PA, a deleted user or a
    // changed password stops working within a minute of the change.
    public static class AuthDirectory
    {
        private static readonly MemoryCache Cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 5000 });
        private sealed record Entry(AuthIdentity? Identity, string StampTag);

        public static AuthIdentity? Resolve(Context db, char kind, long id, string stampTag)
        {
            string key = kind + ":" + id;
            if (!Cache.TryGetValue(key, out Entry? e) || e == null)
            {
                e = Load(db, kind, id);
                Cache.Set(key, e, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60) });
            }
            return e.Identity != null && e.StampTag == stampTag ? e.Identity : null;
        }

        public static void Forget(char kind, long id) => Cache.Remove(kind + ":" + id);

        private static Entry Load(Context db, char kind, long id)
        {
            if (kind == 'A')
            {
                var a = db.Agents.AsNoTracking().FirstOrDefault(x => x.Id == id);
                return a == null ? new Entry(null, "") : new Entry(new AuthIdentity { UserId = a.Id, Role = "AGENT" }, AuthToken.StampTag(a.Password));
            }

            var u = db.Users.AsNoTracking().FirstOrDefault(x => x.Id == id);
            if (u == null) return new Entry(null, "");
            string tag = AuthToken.StampTag(u.SecurityStamp);
            string role = (u.UserType ?? "").ToUpperInvariant();

            switch (role)
            {
                case "DOCTOR":
                    var d = db.Doctors.AsNoTracking().FirstOrDefault(x => x.UserId == u.Id);
                    return new Entry(d == null ? null : new AuthIdentity { UserId = u.Id, Role = role, DoctorId = d.Id }, tag);
                case "PA":
                    var pa = db.PersonalAssistant.AsNoTracking().FirstOrDefault(x => x.UserId == u.Id);
                    return new Entry(pa == null || !pa.IsActive || !pa.IsVerified ? null
                        : new AuthIdentity { UserId = u.Id, Role = role, DoctorId = pa.DoctorId, PaId = pa.Id }, tag);
                case "MANAGER":
                    var m = db.Manager.AsNoTracking().FirstOrDefault(x => x.UserId == u.Id);
                    return new Entry(m == null || !m.IsActive || !m.IsVerified ? null
                        : new AuthIdentity { UserId = u.Id, Role = role, DoctorId = m.DoctorId, ManagerId = m.Id }, tag);
                default:
                    return new Entry(new AuthIdentity { UserId = u.Id, Role = role }, tag);
            }
        }
    }

    // Reads the bearer token on every request, attaches the identity, and (in Enforce mode)
    // rejects requests that have none unless the endpoint is on the public list.
    public class AuthMiddleware
    {
        private readonly RequestDelegate _next;
        private static readonly ConcurrentDictionary<string, int> LogCounts = new();

        public AuthMiddleware(RequestDelegate next) { _next = next; }

        public async Task Invoke(HttpContext ctx)
        {
            if (string.Equals(AuthContext.Mode, "Off", StringComparison.OrdinalIgnoreCase)
                || HttpMethods.IsOptions(ctx.Request.Method))
            {
                await _next(ctx);
                return;
            }

            string path = ctx.Request.Path.Value ?? "";
            AuthIdentity? identity = null;
            string? token = ReadToken(ctx);

            if (token != null
                && AuthToken.TryValidate(token, AuthContext.Secret, out char kind, out long id, out string tag, out long issuedAt))
            {
                var db = ctx.RequestServices.GetRequiredService<Context>();
                identity = AuthDirectory.Resolve(db, kind, id, tag);
                if (identity != null)
                {
                    ctx.Items["auth.identity"] = identity;
                    double ageDays = (DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(issuedAt)).TotalDays;
                    if (ageDays > AuthToken.RefreshAfterDays)
                    {
                        string fresh = kind == 'A'
                            ? AuthToken.Issue('A', id, db.Agents.AsNoTracking().Where(a => a.Id == id).Select(a => a.Password).FirstOrDefault(), AuthContext.Secret)
                            : AuthToken.Issue('U', id, db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.SecurityStamp).FirstOrDefault(), AuthContext.Secret);
                        ctx.Response.Headers["X-Auth-Token"] = fresh;
                    }
                }
            }

            if (identity != null && !RoleAccess.Allows(identity.Role, ctx.Request.Method, path))
            {
                if (AuthContext.Enforcing)
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.WriteAsync("{\"IsSuccess\":false,\"Message\":\"You do not have access to this.\"}");
                    return;
                }
                string rk = identity.Role + " " + ctx.Request.Method + " " + RouteShape(path);
                int rn = LogCounts.AddOrUpdate(rk, 1, (_, c) => c + 1);
                if (rn == 1 || rn % 200 == 0)
                    Console.WriteLine($"[AUTH-LOG] role not allowed: {rk} (x{rn})");
            }

            // Anonymous QR-verification lookups: throttle callers who keep asking for records that
            // do not exist (someone walking MR numbers). Signed-in staff are not counted.
            string? verifyIp = null;
            if (identity == null && VerifyThrottle.Applies(ctx.Request.Method, path))
            {
                verifyIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                if (VerifyThrottle.TryGetBlock(verifyIp, out var retryAfter))
                {
                    ctx.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    ctx.Response.Headers["Retry-After"] = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.WriteAsync("{\"message\":\"Too many failed attempts. Try again later.\"}");
                    return;
                }
            }

            if (identity == null && !PublicEndpoints.IsPublic(ctx.Request.Method, path))
            {
                if (AuthContext.Enforcing)
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.WriteAsync("{\"IsSuccess\":false,\"Unauthorized\":true,\"Message\":\"Session expired. Please sign in again.\"}");
                    return;
                }

                // Log mode: one line for the first hit of each route, then every 200th, so the
                // log shows which callers still send no token without flooding.
                string key = ctx.Request.Method + " " + RouteShape(path);
                int n = LogCounts.AddOrUpdate(key, 1, (_, c) => c + 1);
                if (n == 1 || n % 200 == 0)
                    Console.WriteLine($"[AUTH-LOG] no valid token: {key} (x{n})");
            }

            await _next(ctx);

            if (verifyIp != null && ctx.Response.StatusCode == StatusCodes.Status404NotFound)
                VerifyThrottle.Fail(verifyIp);
        }

        // "Authorization: Bearer x" for API calls; "?access_token=x" for plain GET links (window.open, <a>)
        // that cannot carry a header.
        private static string? ReadToken(HttpContext ctx)
        {
            string h = ctx.Request.Headers["Authorization"].ToString();
            if (h.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return h.Substring(7).Trim();
            if (HttpMethods.IsGet(ctx.Request.Method))
            {
                string q = ctx.Request.Query["access_token"].ToString();
                if (!string.IsNullOrEmpty(q)) return q;
            }
            return null;
        }

        // Collapse numeric path segments so /api/child/17922 and /api/child/18006 count as one route.
        private static string RouteShape(string path) =>
            string.Join("/", path.Split('/').Select(s => s.Length > 0 && s.All(char.IsDigit) ? "{n}" : s));
    }

    // Endpoints that must work without a login: sign-in itself, password recovery, the QR
    // verification pages and the self-signup flow. Everything else needs a token.
    public static class PublicEndpoints
    {
        public static bool IsPublic(string method, string rawPath)
        {
            string p = rawPath.TrimEnd('/').ToLowerInvariant();
            bool get = method == "GET", post = method == "POST";

            // Static files and Swagger are not API routes.
            if (!p.StartsWith("/api/") && !p.StartsWith("/forget")) return true;

            // Sign-in and recovery
            if (post && (p == "/api/user/login" || p == "/api/user/forgot-password" || p == "/api/user/verify" || p == "/api/agent/login"))
                return true;
            // The agent's first-login password change proves identity with phone + current password.
            if (method == "PUT" && p == "/api/agent/change-password") return true;
            if (get && (p == "/api/user/link-login" || p == "/api/user/link-login-pa" || p == "/api/user/validate-session"))
                return true;
            if (get && (p.StartsWith("/forget/") || p.StartsWith("/forgetemail/")))
                return true;

            // QR verification pages opened from a printed certificate or invoice
            if (get && p.Contains("verif")) return true;

            // Self sign-up: doctor registration, its image upload, city list, parent/agent self registration
            if (post && (p == "/api/doctor" || p == "/api/upload" || p == "/api/child/agent-register")) return true;
            if (post && p.StartsWith("/api/doctor/") && p.EndsWith("/update-images")) return true;
            if (post && (p == "/api/personalassistant/signup" || p == "/api/manager/signup")) return true;
            if (get && (p == "/api/doctor/alldoc" || p == "/api/doctor/with-clinics")) return true;
            if (get && (p == "/api/city" || p == "/api/city/names" || p == "/api/vaccineinfo" || p.StartsWith("/api/vaccineinfo/")))
                return true;

            return false;
        }
    }

    // What each kind of login may call. Doctors, PAs, managers and the super admin reach every
    // endpoint (the guards inside the controllers narrow that further). Parents and agents get
    // read access to the screens their app has plus the few writes their app actually makes.
    public static class RoleAccess
    {
        private static readonly string[] ParentReadable =
            { "child", "booking", "doctor", "doctorschedule", "clinic", "vaccineinfo", "notification", "homecity", "vaccine", "brand" };
        private static readonly string[] AgentReadable =
            { "child", "agent", "booking", "doctor", "doctorschedule", "clinic", "vaccineinfo", "referral" };

        public static bool Allows(string role, string method, string rawPath)
        {
            if (role == "DOCTOR" || role == "PA" || role == "MANAGER" || role == "SUPERADMIN") return true;
            if (role != "PARENT" && role != "AGENT") return false;

            string p = rawPath.TrimEnd('/').ToLowerInvariant();
            if (!p.StartsWith("/api/")) return true;
            var seg = p.Substring(5).Split('/');
            string controller = seg[0];

            // Of the schedule endpoints, parents and agents only read one dose row and reschedule.
            if (controller == "schedule" && (method == "GET" || method == "HEAD") && seg.Length == 2 && seg[1].All(char.IsDigit))
                return true;

            if (method == "GET" || method == "HEAD")
                return System.Array.IndexOf(role == "PARENT" ? ParentReadable : AgentReadable, controller) >= 0;

            // Writes each app really makes
            if (method == "POST" && p == "/api/booking") return true;
            if (method == "POST" && (p == "/api/device/register" || p == "/api/device/unregister")) return true;
            if (method == "POST" && p == "/api/child/followup") return true;
            if (method == "PATCH" && p == "/api/doctor/update-clinic-id") return true;
            if (method == "PUT" && p == "/api/schedule/reschedule") return true;
            if (role == "PARENT")
            {
                if (method == "PUT" && controller == "notification") return true;
                if (method == "PUT" && p.StartsWith("/api/booking/") && p.EndsWith("/parent-cancel")) return true;
                if (method == "POST" && p == "/api/user/change-password") return true;
            }
            else
            {
                if (method == "POST" && p == "/api/child/agent-register") return true;
                // Two-step registration: the agent files a referral. Staff-side referral endpoints
                // stay closed to agents by their own [RolesOnly].
                if (method == "POST" && p == "/api/referral") return true;
            }
            return false;
        }
    }

    // Slows down password guessing: after 8 wrong passwords for one account in 15 minutes,
    // that account's login is refused for 15 minutes.
    public static class LoginThrottle
    {
        private const int MaxFails = 8;
        private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
        private static readonly ConcurrentDictionary<string, (int fails, DateTime since)> State = new();

        public static bool IsBlocked(string key)
        {
            if (!State.TryGetValue(key, out var s)) return false;
            if (DateTime.UtcNow - s.since > Window) { State.TryRemove(key, out _); return false; }
            return s.fails >= MaxFails;
        }

        public static void Fail(string key) =>
            State.AddOrUpdate(key, (1, DateTime.UtcNow),
                (_, s) => DateTime.UtcNow - s.since > Window ? (1, DateTime.UtcNow) : (s.fails + 1, s.since));

        public static void Success(string key) => State.TryRemove(key, out _);
    }

    // Slows down record guessing on the anonymous verify routes (VerifyRecord, VerifyInvoice, the
    // verification PDFs). Counted per client IP; a "failure" is a lookup that found nothing (404).
    //   3 failures within 5 minutes -> suspended 15 min
    //   first failure after that    -> 1 hour, then 6 hours, then 24 hours (24 hours from there on)
    // The ladder is forgotten after 24 hours with no new suspension.
    public static class VerifyThrottle
    {
        private const int FreeFails = 3;
        private static readonly TimeSpan FailWindow = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan[] Steps =
            { TimeSpan.FromMinutes(15), TimeSpan.FromHours(1), TimeSpan.FromHours(6), TimeSpan.FromHours(24) };
        private static readonly TimeSpan Forget = TimeSpan.FromHours(24);

        private sealed class Entry
        {
            public readonly List<DateTime> Fails = new();
            public int Level;                 // suspensions served so far
            public DateTime BlockedUntil;
        }

        private static readonly ConcurrentDictionary<string, Entry> State = new();
        private static DateTime _lastSweep = DateTime.UtcNow;

        public static bool Applies(string method, string rawPath)
        {
            if (!HttpMethods.IsGet(method)) return false;
            string p = rawPath.ToLowerInvariant();
            return p.StartsWith("/api/") && p.Contains("verif");
        }

        public static bool TryGetBlock(string ip, out TimeSpan remaining)
        {
            remaining = TimeSpan.Zero;
            if (!State.TryGetValue(ip, out var e)) return false;
            lock (e)
            {
                var left = e.BlockedUntil - DateTime.UtcNow;
                if (left <= TimeSpan.Zero) return false;
                remaining = left;
                return true;
            }
        }

        public static void Fail(string ip)
        {
            var now = DateTime.UtcNow;
            Sweep(now);
            var e = State.GetOrAdd(ip, _ => new Entry());
            lock (e)
            {
                if (e.BlockedUntil > now) return;   // already suspended; a parallel request must not stack a step

                // Strikes are forgotten after a clean day.
                if (e.Level > 0 && now - e.BlockedUntil > Forget) { e.Level = 0; e.Fails.Clear(); }

                e.Fails.RemoveAll(t => now - t > FailWindow);
                e.Fails.Add(now);

                // First suspension needs FreeFails misses; once on the ladder, one miss is enough.
                if (e.Level == 0 && e.Fails.Count < FreeFails) return;

                e.BlockedUntil = now + Steps[Math.Min(e.Level, Steps.Length - 1)];
                e.Level++;
                e.Fails.Clear();
                Console.WriteLine($"[VERIFY-THROTTLE] {ip} suspended until {e.BlockedUntil:u} (step {e.Level})");
            }
        }

        private static void Sweep(DateTime now)
        {
            if (now - _lastSweep < TimeSpan.FromMinutes(10)) return;
            _lastSweep = now;
            foreach (var kv in State)
            {
                lock (kv.Value)
                {
                    bool idle = kv.Value.BlockedUntil + Forget < now
                                && kv.Value.Fails.All(t => now - t > FailWindow);
                    if (idle) State.TryRemove(kv.Key, out _);
                }
            }
        }

        // For tests.
        public static void Reset() => State.Clear();
    }
}
