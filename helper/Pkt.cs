using System;
using System.Globalization;

namespace VaccineAPI
{
    // Pakistan Standard Time: fixed UTC+5, no DST. The only time ever exposed or used for day bucketing
    // by the assistant-location module. DB values stay UTC (Unspecified is treated as UTC).
    // Never uses DateTime.Now / DateTime.Today / server local zone / current culture. This is the only file
    // allowed to shift by the PKT offset (a source-scan test enforces it).
    public static class Pkt
    {
        public const int OffsetHours = 5;
        public static readonly TimeSpan Offset = TimeSpan.FromHours(OffsetHours);

        // PKT wall-clock "now" for printing and day maths only. Kind is Unspecified on purpose so it can
        // never serialise as a false UTC instant. Never store this in an instant column.
        public static DateTime NowWall() => DateTime.SpecifyKind(DateTime.UtcNow.Add(Offset), DateTimeKind.Unspecified);

        // Today's PKT calendar date (00:00, Unspecified). The one "today" for every business-day default.
        public static DateTime Today() => NowWall().Date;

        // UTC instant (or Unspecified = UTC) to its PKT wall-clock DateTime, Unspecified kind.
        public static DateTime ToWall(DateTime utcOrUnspecified) =>
            DateTime.SpecifyKind(ToUtc(utcOrUnspecified).Add(Offset), DateTimeKind.Unspecified);

        // Instant as an ISO-8601 string with an explicit Z.
        public static string Iso(DateTime utcOrUnspecified) =>
            ToUtc(utcOrUnspecified).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        public static string Iso(DateTime? v) => v.HasValue ? Iso(v.Value) : null;

        // Format an instant in PKT.
        public static string Format(DateTime utcOrUnspecified, string fmt) =>
            ToWall(utcOrUnspecified).ToString(fmt, CultureInfo.InvariantCulture);

        public static DateTimeOffset Now() => new DateTimeOffset(DateTime.UtcNow, TimeSpan.Zero).ToOffset(Offset);

        // Any DateTime to a UTC DateTime (Unspecified = UTC; Local converted by the runtime).
        public static DateTime ToUtc(DateTime v) =>
            v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc);

        public static DateTimeOffset ToPkt(DateTime utcOrUnspecified) =>
            new DateTimeOffset(ToUtc(utcOrUnspecified), TimeSpan.Zero).ToOffset(Offset);

        public static DateTimeOffset? ToPkt(DateTime? v) => v.HasValue ? ToPkt(v.Value) : (DateTimeOffset?)null;

        public static string DayKey(DateTime utcOrUnspecified) =>
            ToUtc(utcOrUnspecified).Add(Offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        public static string TodayKey() => DayKey(DateTime.UtcNow);

        // Strict "yyyy-MM-dd" -> the PKT calendar day (Date at 00:00, Unspecified kind).
        // Years outside 2000..2099 are rejected so later UTC/AddDays arithmetic can never overflow.
        public static bool TryParseDay(string? s, out DateTime day) =>
            DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day)
            && day.Year >= 2000 && day.Year <= 2099;

        // UTC instant at which the given PKT calendar day starts.
        public static DateTime DayStartUtc(DateTime pktDay) =>
            DateTime.SpecifyKind(pktDay.Date - Offset, DateTimeKind.Utc);

        public static DateTime DayStartUtc(string yyyyMMdd)
        {
            if (!TryParseDay(yyyyMMdd, out var d)) throw new FormatException("Expected yyyy-MM-dd.");
            return DayStartUtc(d);
        }

        // [start, end) UTC window of one PKT day.
        public static (DateTime fromUtc, DateTime toUtc) DayWindowUtc(DateTime pktDay)
        {
            var s = DayStartUtc(pktDay);
            return (s, s.AddDays(1));
        }

        public static DateTime TodayStartUtc() => DayStartUtc(DateTime.UtcNow.Add(Offset).Date);
    }
}
