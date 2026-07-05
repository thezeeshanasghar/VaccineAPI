using System;

namespace VaccineAPI
{
    // Single source of truth for "now" / "today" in the clinic's local time.
    // The clinic operates in Pakistan Standard Time (PST = Pakistan Standard Time,
    // UTC+5, no DST). The server and DB run on UTC; business dates are PKT.
    //
    // Use these instead of scattering DateTime.UtcNow.AddHours(5) / DateTime.Now
    // through the stock module. If the offset ever needs to change, it changes here.
    public static class ClinicClock
    {
        // Pakistan is a fixed UTC+5 with no daylight saving.
        public const int PktOffsetHours = 5;

        // Current PKT wall-clock time (date + time).
        public static DateTime NowPkt() => DateTime.UtcNow.AddHours(PktOffsetHours);

        // Today's PKT calendar date (time stripped). This is the authoritative
        // "today" for the stock date policy — clients never compute it locally.
        public static DateTime TodayPkt() => NowPkt().Date;

        // Convert a UTC DateTime to its PKT wall-clock equivalent.
        public static DateTime ToPkt(DateTime utc) => utc.AddHours(PktOffsetHours);
    }
}
