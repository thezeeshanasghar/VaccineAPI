using System;
using System.Collections;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using VaccineAPI.Models;

namespace VaccineAPI
{
    public enum OwnerKind
    {
        Child,      // the child belongs to the caller's practice (or is the parent's own child)
        Clinic,     // the clinic belongs to the caller's doctor
        Doctor,     // the doctor id is the caller's own practice
        Agent,      // the agent id is the caller's own (agents only)
        UserSelf,   // a parent may only ask about their own user id; staff are not restricted
        Schedule,   // the schedule row's child belongs to the caller's practice
        Pa,         // the PA works for the caller's doctor (a PA may only name themselves)
        Manager     // the manager works for the caller's doctor (a manager may only name themselves)
    }

    // Checks that the id(s) an action receives belong to whoever is calling, using the session
    // token. The id is looked up by name among the action's parameters, then among the properties
    // of any body object, so it works for both "?childId=5" and a DTO with a ChildId field.
    // A list of ids (e.g. arr[]) is checked item by item.
    //
    // Without a token the check passes while the API is in Log mode (older apps) and fails in
    // Enforce mode (the middleware has already rejected those requests anyway).
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
    public sealed class OwnsAttribute : ActionFilterAttribute
    {
        private readonly OwnerKind _kind;
        private readonly string[] _names;

        public OwnsAttribute(OwnerKind kind, params string[] names) { _kind = kind; _names = names; }

        public override void OnActionExecuting(ActionExecutingContext ctx)
        {
            var id = AuthContext.Current;
            // No identity: either an older app in Log mode, or a public endpoint. In Enforce mode the
            // middleware has already turned away every non-public request without a token.
            if (id == null) return;
            if (id.Role == "SUPERADMIN") return;

            var db = ctx.HttpContext.RequestServices.GetRequiredService<Context>();
            foreach (var name in _names)
            {
                foreach (long value in FindValues(ctx, name))
                {
                    if (value <= 0) continue;
                    if (!Check(db, id, value))
                    {
                        if (AuthContext.Enforcing) { Deny(ctx); return; }
                        Console.WriteLine($"[AUTH-LOG] ownership would be denied: {id.Role} {_kind}({name}={value}) on {ctx.HttpContext.Request.Method} {ctx.HttpContext.Request.Path}");
                    }
                }
            }
        }

        private bool Check(Context db, AuthIdentity id, long value)
        {
            switch (_kind)
            {
                case OwnerKind.Child: return CallerGuard.OwnsChild(db, value);
                case OwnerKind.Clinic: return CallerGuard.OwnsClinic(db, value);
                case OwnerKind.Doctor: return CallerGuard.OwnsDoctor(value);
                case OwnerKind.Agent: return id.Role == "AGENT" ? id.UserId == value : id.Role != "PARENT";
                case OwnerKind.UserSelf: return id.Role != "PARENT" && id.Role != "AGENT" || id.UserId == value;
                case OwnerKind.Pa:
                    if (id.Role == "PA") return id.PaId == value;
                    if (id.Role == "PARENT" || id.Role == "AGENT") return false;
                    return db.PersonalAssistant.Any(x => x.Id == value && x.DoctorId == id.DoctorId);
                case OwnerKind.Manager:
                    if (id.Role == "MANAGER") return id.ManagerId == value;
                    if (id.Role == "PARENT" || id.Role == "AGENT") return false;
                    return db.Manager.Any(x => x.Id == value && x.DoctorId == id.DoctorId);
                case OwnerKind.Schedule:
                    var childId = db.Schedules.Where(s => s.Id == value).Select(s => (long?)s.ChildId).FirstOrDefault();
                    return childId.HasValue && CallerGuard.OwnsChild(db, childId.Value);
                default: return false;
            }
        }

        private static System.Collections.Generic.IEnumerable<long> FindValues(ActionExecutingContext ctx, string name)
        {
            foreach (var kv in ctx.ActionArguments)
            {
                if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
                    foreach (var v in ToLongs(kv.Value)) yield return v;
            }
            foreach (var kv in ctx.ActionArguments)
            {
                if (kv.Value == null || kv.Value is string || kv.Value.GetType().IsPrimitive) continue;
                // A body that is a list of objects: read the property from each item.
                var items = kv.Value is IEnumerable en && !(kv.Value is string) && !IsScalarList(kv.Value)
                    ? en.Cast<object?>() : new object?[] { kv.Value };
                foreach (var item in items)
                {
                    if (item == null) continue;
                    var prop = item.GetType().GetProperty(name,
                        System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (prop != null)
                        foreach (var v in ToLongs(prop.GetValue(item))) yield return v;
                }
            }
        }

        private static bool IsScalarList(object o)
        {
            var t = o.GetType();
            var el = t.IsArray ? t.GetElementType() : t.IsGenericType ? t.GetGenericArguments()[0] : null;
            return el != null && (el.IsPrimitive || el == typeof(string));
        }

        private static System.Collections.Generic.IEnumerable<long> ToLongs(object? o)
        {
            if (o == null) yield break;
            if (o is string s) { if (long.TryParse(s, out long l)) yield return l; yield break; }
            if (o is IEnumerable e) { foreach (var x in e) foreach (var v in ToLongs(x)) yield return v; yield break; }
            if (o is IConvertible c && !(o is bool))
            {
                long l2 = 0; bool ok = true;
                try { l2 = c.ToInt64(null); } catch { ok = false; }
                if (ok) yield return l2;
            }
        }

        internal static void Deny(ActionExecutingContext ctx)
        {
            ctx.Result = new JsonResult(new { IsSuccess = false, Message = "This record belongs to another practice or account." }) { StatusCode = 403 };
        }
    }

    // Only the listed kinds of login may call the action (e.g. the all-children list is super admin only).
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public sealed class RolesOnlyAttribute : ActionFilterAttribute
    {
        private readonly string[] _roles;
        public RolesOnlyAttribute(params string[] roles) { _roles = roles; }

        public override void OnActionExecuting(ActionExecutingContext ctx)
        {
            var id = AuthContext.Current;
            if (id == null) return;
            if (!_roles.Contains(id.Role))
            {
                if (!AuthContext.Enforcing)
                {
                    Console.WriteLine($"[AUTH-LOG] role would be denied: {id.Role} on {ctx.HttpContext.Request.Method} {ctx.HttpContext.Request.Path}");
                    return;
                }
                ctx.Result = new JsonResult(new { IsSuccess = false, Message = "You do not have access to this." }) { StatusCode = 403 };
            }
        }
    }
}
