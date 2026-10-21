using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using VaccineAPI.Tests.Infrastructure;
using Xunit;

namespace VaccineAPI.Tests.Cases;

/// <summary>
/// TIMESTAMP_SPEC contract: instants are UTC in the DB and leave the API with a Z; days never carry a zone;
/// "now" and "today" come from Pkt only.
/// </summary>
public class TimeContractTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "VaccineAPI.csproj"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    private static readonly Regex Banned = new(
        @"DateTime\.Now\b|DateTime\.Today\b|AddHours\(\s*5\s*\)|ClinicClock|FindSystemTimeZoneById|TimeZoneInfo\.ConvertTimeFromUtc");

    [Fact]
    public void NoWallClockOrHandRolledPktShiftOutsidePktCs()
    {
        var root = RepoRoot();
        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (rel.StartsWith("bin/") || rel.StartsWith("obj/") || rel.StartsWith("VaccineAPI.Tests/")
                || rel.StartsWith("Migrations/") || rel == "helper/Pkt.cs") continue;
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var t = lines[i].TrimStart();
                if (t.StartsWith("//")) continue;
                if (Banned.IsMatch(lines[i])) violations.Add($"{rel}:{i + 1}: {t}");
            }
        }
        Assert.True(violations.Count == 0, "Use Pkt (helper/Pkt.cs) or DateTime.UtcNow:\n" + string.Join("\n", violations));
    }

    private static bool IsInstantName(string n) =>
        n.EndsWith("At") || n is "ActionDate" or "ApprovalDate" or "PaidDate" or "ValidUpto";

    [Fact]
    public void EveryInstantColumnHasTheUtcConverter()
    {
        using var t = new TestDb();
        using var db = t.NewContext();
        var missing = new List<string>();
        foreach (var et in db.Model.GetEntityTypes())
            foreach (var p in et.GetProperties())
            {
                if (p.ClrType != typeof(DateTime) && p.ClrType != typeof(DateTime?)) continue;
                if (IsInstantName(p.Name) && p.GetValueConverter() == null) missing.Add(et.ClrType.Name + "." + p.Name);
                if (!IsInstantName(p.Name) && p.GetValueConverter() != null) missing.Add("unexpected converter " + et.ClrType.Name + "." + p.Name);
            }
        Assert.Empty(missing);
    }

    [Fact]
    public void InstantReadFromDbSerialisesWithZAndDateOnlyDoesNot()
    {
        using var t = new TestDb();
        using var db = t.NewContext();
        var at = db.Model.FindEntityType(typeof(VaccineAPI.Models.Notification))!.FindProperty("CreatedAt")!;
        var fromDb = (DateTime)at.GetValueConverter()!.ConvertFromProvider(new DateTime(2026, 10, 21, 4, 30, 0, DateTimeKind.Unspecified))!;
        Assert.Equal(DateTimeKind.Utc, fromDb.Kind);
        Assert.Equal("\"2026-10-21T04:30:00Z\"", JsonConvert.SerializeObject(fromDb));

        var dob = db.Model.FindEntityType(typeof(VaccineAPI.Models.Child))!.FindProperty("DOB")!;
        Assert.Null(dob.GetValueConverter());
    }

    [Fact]
    public void PktHelperIsFixedPlusFive()
    {
        var utc = new DateTime(2026, 10, 20, 19, 30, 0, DateTimeKind.Utc);   // 00:30 PKT next day
        Assert.Equal("2026-10-21", Pkt.DayKey(utc));
        Assert.Equal(new DateTime(2026, 10, 21, 0, 30, 0), Pkt.ToWall(utc));
        Assert.Equal("2026-10-20T19:30:00Z", Pkt.Iso(utc));
        Assert.Equal("21 Oct 2026 12:30 AM", Pkt.Format(utc, "dd MMM yyyy hh:mm tt"));
    }
}
