using System;
using System.Collections.Generic;
using System.Linq;

namespace VaccineAPI
{
    public struct KmFix
    {
        public long ShiftId;
        public double Lat, Lon;
        public double? Accuracy;
        public DateTime At;   // UTC
    }

    public class KmResult
    {
        public double Km;            // unrounded
        public int Used;             // usable fixes
        public int Skipped;          // rejected fixes
        public bool Gaps;            // a >15 min silence was not bridged
        public double CoveredSeconds;// time between consecutive usable fixes (<= max gap) within a shift
        public bool[] Usable = new bool[0];   // per input fix (in the order passed in)
    }

    // The ONE km calculation, shared by the km report and the trail. Works on unrounded values;
    // callers round once at the end.
    public static class KmCalc
    {
        public const double MaxAccuracyM = 50;
        public const double MinStepM = 25;
        public const double MaxGapMinutes = 15;
        public const double MaxSpeedKmh = 100;
        public const int ReanchorAfter = 3;

        public static double Haversine(double la1, double lo1, double la2, double lo2)
        {
            double r(double x) => x * Math.PI / 180;
            var dLat = r(la2 - la1); var dLon = r(lo2 - lo1);
            var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                  + Math.Cos(r(la1)) * Math.Cos(r(la2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return 6371 * 2 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1 - h));
        }

        public static KmResult Compute(IList<KmFix> fixes)
        {
            var res = new KmResult { Usable = new bool[fixes.Count] };
            var order = Enumerable.Range(0, fixes.Count).OrderBy(i => fixes[i].ShiftId).ThenBy(i => fixes[i].At).ToList();

            long curShift = long.MinValue;
            bool have = false;
            double aLat = 0, aLon = 0, aAcc = 0; DateTime aAt = default;   // anchor
            DateTime lastUsableAt = default;
            int rejected = 0;

            foreach (var i in order)
            {
                var x = fixes[i];
                if (x.ShiftId != curShift) { curShift = x.ShiftId; have = false; rejected = 0; }

                // null accuracy is suspect: treated as 100 m, i.e. rejected by the cutoff
                var acc = x.Accuracy ?? 100;
                if (double.IsNaN(acc) || acc < 0 || acc > MaxAccuracyM
                    || double.IsNaN(x.Lat) || double.IsNaN(x.Lon))
                { res.Skipped++; continue; }

                if (!have)
                {
                    have = true; aLat = x.Lat; aLon = x.Lon; aAcc = acc; aAt = x.At; lastUsableAt = x.At; rejected = 0;
                    res.Usable[i] = true; res.Used++;
                    continue;
                }

                var sinceLast = (x.At - lastUsableAt).TotalMinutes;
                if (sinceLast <= 0) { res.Skipped++; continue; }   // out of order / same instant

                if (sinceLast > MaxGapMinutes)
                {   // silence: never bridged, re-anchor here and flag
                    res.Gaps = true;
                    aLat = x.Lat; aLon = x.Lon; aAcc = acc; aAt = x.At; lastUsableAt = x.At; rejected = 0;
                    res.Usable[i] = true; res.Used++;
                    continue;
                }

                var step = Haversine(aLat, aLon, x.Lat, x.Lon);
                // speed is judged over the time since the previous usable fix, not since the anchor: after hours of
                // sub-threshold jitter the anchor is old and would make any glitch look slow.
                var hours = (x.At - lastUsableAt).TotalHours;
                var speed = hours > 0 ? step / hours : double.PositiveInfinity;
                if (speed > MaxSpeedKmh)
                {
                    rejected++;
                    res.Skipped++;
                    if (rejected >= ReanchorAfter)
                    {   // we have probably been teleported/lost the old anchor: restart from here
                        aLat = x.Lat; aLon = x.Lon; aAcc = acc; aAt = x.At; lastUsableAt = x.At; rejected = 0;
                        res.Skipped--; res.Usable[i] = true; res.Used++;
                    }
                    continue;
                }

                rejected = 0;
                res.CoveredSeconds += (x.At - lastUsableAt).TotalSeconds;
                lastUsableAt = x.At;
                res.Usable[i] = true; res.Used++;

                var thresholdKm = Math.Max(MinStepM, aAcc + acc) / 1000.0;
                if (step > thresholdKm)
                {
                    res.Km += step;
                    aLat = x.Lat; aLon = x.Lon; aAcc = acc; aAt = x.At;
                }
                // sub-threshold: jitter, anchor stays put
            }
            return res;
        }
    }
}
