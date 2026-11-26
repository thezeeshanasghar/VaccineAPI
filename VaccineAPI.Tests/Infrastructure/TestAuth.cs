using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace VaccineAPI.Tests.Infrastructure;

/// <summary>
/// Runs one controller call as a signed-in user (the identity AuthMiddleware would attach from the
/// login token), then restores whatever was there before. PAAssignmentController refuses calls with
/// no token, so tests that call it directly must say who is calling. Classes using this belong in
/// the "AuthStatics" collection, because AuthContext.Accessor is static.
/// </summary>
public static class TestAuth
{
    public static AuthIdentity Doctor(long doctorId) => new AuthIdentity { UserId = 0, Role = "DOCTOR", DoctorId = doctorId };
    public static AuthIdentity Pa(long paId, long doctorId) => new AuthIdentity { UserId = 0, Role = "PA", DoctorId = doctorId, PaId = paId };
    public static AuthIdentity Manager(long managerId, long doctorId) => new AuthIdentity { UserId = 0, Role = "MANAGER", DoctorId = doctorId, ManagerId = managerId };

    public static async Task<T> As<T>(AuthIdentity? id, Func<Task<T>> call)
    {
        var saved = AuthContext.Accessor;
        var ctx = new DefaultHttpContext();
        if (id != null) ctx.Items["auth.identity"] = id;
        AuthContext.Accessor = new HttpContextAccessor { HttpContext = ctx };
        try { return await call(); }
        finally { AuthContext.Accessor = saved; }
    }
}
