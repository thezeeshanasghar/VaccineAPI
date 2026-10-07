using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using VaccineAPI.Tests.Infrastructure;
using Xunit;

namespace VaccineAPI.Tests.Cases;

/// <summary>Session token, public endpoint list, role matrix and the token-aware stock guard.</summary>
public class AuthTests
{
    private const string Secret = "unit-test-secret-unit-test-secret-1234";

    // ---- token ----

    [Fact]
    public void Token_RoundTrips_AndCarriesNoRawStamp()
    {
        string t = AuthToken.Issue('U', 42, "stamp-abc", Secret);
        Assert.True(AuthToken.TryValidate(t, Secret, out char kind, out long id, out string tag, out long iat));
        Assert.Equal('U', kind); Assert.Equal(42, id);
        Assert.Equal(AuthToken.StampTag("stamp-abc"), tag);
        Assert.DoesNotContain("stamp-abc", t);
    }

    [Fact]
    public void Token_RejectsTamperingWrongSecretAndExpiry()
    {
        string t = AuthToken.Issue('U', 42, "s", Secret);
        // flip the user id inside the payload: signature no longer matches
        var parts = t.Split('.');
        string forged = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("U:1:0:99999999999:x")).TrimEnd('=') + "." + parts[1];
        Assert.False(AuthToken.TryValidate(forged, Secret, out _, out _, out _, out _));
        Assert.False(AuthToken.TryValidate(t, Secret + "x", out _, out _, out _, out _));
        Assert.False(AuthToken.TryValidate("", Secret, out _, out _, out _, out _));
        Assert.False(AuthToken.TryValidate("garbage", Secret, out _, out _, out _, out _));

        string old = AuthToken.Issue('U', 42, "s", Secret, DateTimeOffset.UtcNow.AddDays(-(AuthToken.ValidDays + 1)));
        Assert.False(AuthToken.TryValidate(old, Secret, out _, out _, out _, out _));
    }

    [Fact]
    public void Token_StampTagChangesWhenStampChanges()
    {
        Assert.NotEqual(AuthToken.StampTag("a"), AuthToken.StampTag("b"));
    }

    // ---- public list ----

    [Theory]
    [InlineData("POST", "/api/user/login", true)]
    [InlineData("POST", "/api/user/forgot-password", true)]
    [InlineData("GET", "/api/user/link-login", true)]
    [InlineData("GET", "/api/child/VerifyRecord", true)]
    [InlineData("GET", "/api/child/17/ScheduleVerify", true)]
    [InlineData("POST", "/api/doctor", true)]
    [InlineData("GET", "/Resources/x.pdf", true)]
    [InlineData("GET", "/api/user", false)]
    [InlineData("GET", "/api/user/5", false)]
    [InlineData("PUT", "/api/user/5", false)]
    [InlineData("GET", "/api/child", false)]
    [InlineData("GET", "/api/child/5/schedule", false)]
    [InlineData("PUT", "/api/brandamount", false)]
    [InlineData("POST", "/api/adjuststock", false)]
    [InlineData("GET", "/api/user/1/2000-01-01", false)]
    public void PublicEndpoints_AreExactlyTheListedOnes(string method, string path, bool expected)
        => Assert.Equal(expected, PublicEndpoints.IsPublic(method, path));

    // ---- role matrix ----

    [Theory]
    [InlineData("DOCTOR", "POST", "/api/adjuststock", true)]
    [InlineData("PA", "GET", "/api/supplier", true)]
    [InlineData("PARENT", "GET", "/api/child/5/schedule", true)]
    [InlineData("PARENT", "GET", "/api/schedule/12", true)]
    [InlineData("PARENT", "PUT", "/api/schedule/Reschedule", true)]
    [InlineData("PARENT", "PUT", "/api/schedule/child-schedule", false)]   // give a dose
    [InlineData("PARENT", "GET", "/api/schedule/alert/1/2", false)]
    [InlineData("PARENT", "POST", "/api/adjuststock", false)]
    [InlineData("PARENT", "GET", "/api/supplier", false)]
    [InlineData("PARENT", "POST", "/api/booking", true)]
    [InlineData("PARENT", "PUT", "/api/booking/9/parent-cancel", true)]
    [InlineData("PARENT", "PUT", "/api/booking/9/confirm", false)]
    [InlineData("AGENT", "POST", "/api/child/agent-register", true)]
    [InlineData("AGENT", "GET", "/api/agent/3/report", true)]
    [InlineData("AGENT", "POST", "/api/bill", false)]
    [InlineData("AGENT", "DELETE", "/api/child/5", false)]
    public void RoleAccess_Matrix(string role, string method, string path, bool expected)
        => Assert.Equal(expected, RoleAccess.Allows(role, method, path));

    // ---- stock guard with a token ----

    private static void WithIdentity(AuthIdentity? id, Action body)
    {
        var saved = AuthContext.Accessor;
        var http = new DefaultHttpContext();
        if (id != null) http.Items["auth.identity"] = id;
        AuthContext.Accessor = new HttpContextAccessor { HttpContext = http };
        try { body(); } finally { AuthContext.Accessor = saved; }
    }

    [Fact]
    public void StockGuard_TokenOverridesBody_AndBlocksParentsAgentsAndOtherPractices()
    {
        using var tdb = new TestDb();
        using var db = tdb.NewContext();

        // A parent token can never adjust stock, whatever the body claims.
        WithIdentity(new AuthIdentity { UserId = 5, Role = "PARENT" }, () =>
            Assert.False(StockActionGuard.CheckStockAction(db, null, null, null, null, p => p.StockAdjust, "adjust stock", 1).allowed));

        WithIdentity(new AuthIdentity { UserId = 6, Role = "AGENT" }, () =>
            Assert.False(StockActionGuard.CheckStockAction(db, null, null, null, null, p => p.StockAdjust, "adjust stock", 1).allowed));

        // A doctor token for practice 7 cannot act on practice 8, and cannot dodge the check by leaving PaId empty.
        WithIdentity(new AuthIdentity { UserId = 7, Role = "DOCTOR", DoctorId = 7 }, () =>
        {
            Assert.True(StockActionGuard.CheckStockAction(db, null, null, null, null, p => p.StockAdjust, "adjust stock", 7).allowed);
            Assert.False(StockActionGuard.CheckStockAction(db, null, null, null, null, p => p.StockAdjust, "adjust stock", 8).allowed);
        });

        // A PA token with no PaPermission row is refused even when the body says "doctor" (no PaId).
        WithIdentity(new AuthIdentity { UserId = 9, Role = "PA", DoctorId = 7, PaId = 3 }, () =>
            Assert.False(StockActionGuard.CheckStockAction(db, null, null, null, null, p => p.StockAdjust, "adjust stock", 7).allowed));

        // A manager token is refused even when the body claims no manager.
        WithIdentity(new AuthIdentity { UserId = 10, Role = "MANAGER", DoctorId = 7, ManagerId = 2 }, () =>
            Assert.False(StockActionGuard.CheckStockAction(db, null, null, null, null, p => p.StockAdjust, "adjust stock", 7).allowed));
    }

    [Fact]
    public void CallerGuard_TokenMustMatchTheClaimedUser()
    {
        using var tdb = new TestDb();
        using var db = tdb.NewContext();
        WithIdentity(new AuthIdentity { UserId = 7, Role = "DOCTOR", DoctorId = 7 }, () =>
        {
            Assert.True(CallerGuard.VerifyCaller(db, 7, null));
            Assert.False(CallerGuard.VerifyCaller(db, 8, "anything"));
            Assert.True(CallerGuard.OwnsDoctor(7));
            Assert.False(CallerGuard.OwnsDoctor(8));
        });
    }

    [Fact]
    public void LoginThrottle_BlocksAfterEightFailures_AndResetsOnSuccess()
    {
        string k = "t:" + Guid.NewGuid();
        for (int i = 0; i < 7; i++) LoginThrottle.Fail(k);
        Assert.False(LoginThrottle.IsBlocked(k));
        LoginThrottle.Fail(k);
        Assert.True(LoginThrottle.IsBlocked(k));
        LoginThrottle.Success(k);
        Assert.False(LoginThrottle.IsBlocked(k));
    }
}

public class AuthMiddlewareTests
{
    private const string Secret = "unit-test-secret-unit-test-secret-1234";

    private static async Task<(int status, bool nextCalled, AuthIdentity? identity)> Call(
        Models.Context db, string method, string path, string? bearer, string mode)
    {
        var savedMode = AuthContext.Mode; var savedSecret = AuthContext.Secret;
        AuthContext.Mode = mode; AuthContext.Secret = Secret;
        try
        {
            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            services.AddSingleton(db);
            var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
            ctx.Request.Method = method;
            ctx.Request.Path = path;
            ctx.Response.Body = new MemoryStream();
            if (bearer != null) ctx.Request.Headers["Authorization"] = "Bearer " + bearer;
            bool next = false;
            await new AuthMiddleware(_ => { next = true; return Task.CompletedTask; }).Invoke(ctx);
            ctx.Items.TryGetValue("auth.identity", out var id);
            return (ctx.Response.StatusCode, next, id as AuthIdentity);
        }
        finally { AuthContext.Mode = savedMode; AuthContext.Secret = savedSecret; }
    }

    [Fact]
    public async Task Enforce_RejectsAnonymous_AllowsPublic_AcceptsValidToken_RevokesOnPasswordChange()
    {
        using var w = new StockWorld();
        using var db = w.NewContext();
        var doctor = db.Doctors.Find(w.DoctorId)!;
        var user = db.Users.Find(doctor.UserId)!;
        string token = AuthContext.IssueForUser(user);
        // IssueForUser uses AuthContext.Secret; re-issue with the test secret
        token = AuthToken.Issue('U', user.Id, user.SecurityStamp, Secret);

        var anon = await Call(db, "GET", "/api/supplier", null, "Enforce");
        Assert.Equal(401, anon.status); Assert.False(anon.nextCalled);

        var pub = await Call(db, "POST", "/api/user/login", null, "Enforce");
        Assert.True(pub.nextCalled);

        var ok = await Call(db, "GET", "/api/supplier", token, "Enforce");
        Assert.True(ok.nextCalled);
        Assert.Equal("DOCTOR", ok.identity!.Role);
        Assert.Equal(w.DoctorId, ok.identity.DoctorId);

        // Log mode: anonymous is served (and logged), nothing is rejected.
        var log = await Call(db, "GET", "/api/supplier", null, "Log");
        Assert.True(log.nextCalled);

        // Garbage token behaves like no token.
        var bad = await Call(db, "GET", "/api/supplier", token + "x", "Enforce");
        Assert.Equal(401, bad.status);

        // A parent's token may not reach stock endpoints even though it is valid.
        // (Role matrix is covered separately; here we confirm 403 in Enforce mode.)
        // Changing the password rotates the stamp; the old token must stop working once the cache entry expires.
        user.SecurityStamp = Guid.NewGuid().ToString();
        db.SaveChanges();
        AuthDirectory.Forget('U', user.Id);
        var revoked = await Call(db, "GET", "/api/supplier", token, "Enforce");
        Assert.Equal(401, revoked.status);
    }

    [Fact]
    public async Task Enforce_ParentTokenIsForbiddenFromStaffEndpoints()
    {
        using var w = new StockWorld();
        using var db = w.NewContext();
        var parent = new Models.User { MobileNumber = "3001112223", Password = "x", UserType = "PARENT", CountryCode = "92" };
        db.Users.Add(parent); db.SaveChanges();
        string token = AuthToken.Issue('U', parent.Id, parent.SecurityStamp, Secret);

        var staffRoute = await Call(db, "GET", "/api/supplier", token, "Enforce");
        Assert.Equal(403, staffRoute.status); Assert.False(staffRoute.nextCalled);

        var ownRoute = await Call(db, "GET", "/api/child/user/" + parent.Id, token, "Enforce");
        Assert.True(ownRoute.nextCalled);
    }
}
