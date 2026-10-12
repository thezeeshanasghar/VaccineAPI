using System;
using System.Globalization;

namespace VaccineAPI
{
    // Pakistan Standard Time: fixed UTC+5, no DST. The only time ever exposed or used for day bucketing
    // by the assistant-location module. DB values stay UTC (Unspecified is treated as UTC).
    // Never uses DateTime.Now / server local zone / current culture.
    public static class Pkt
    {
        public static readonly TimeSpan Offset = TimeSpan.FromHours(5);

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
