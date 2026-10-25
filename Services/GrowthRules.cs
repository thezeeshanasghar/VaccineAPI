using System;
using Microsoft.Extensions.Configuration;

namespace VaccineAPI.Services
{
    // Growth requirement for PA-given doses of regular children (GROWTH_FLAG_SPEC.md).
    // One place for the rules so the payment gate and the doctor's reconciliation flag cannot drift.
    public static class GrowthRules
    {
        public const string NotMeasuredActionCode = "GROWTH_NOT_MEASURED";

        // OFC is only recorded under 2 years; from the 2nd birthday it is unfillable and ignored.
        public static bool OfcAllowed(DateTime dob, DateTime givenDate)
            => givenDate.Date < dob.Date.AddYears(2);

        // At least one of weight / height / OFC (OFC only while allowed for the child's age).
        public static bool HasGrowth(float? weight, float? height, float? circle, DateTime dob, DateTime givenDate)
            => weight.GetValueOrDefault() > 0
            || height.GetValueOrDefault() > 0
            || (circle.GetValueOrDefault() > 0 && OfcAllowed(dob, givenDate));

        // Config "Growth:FlagFromDate" (yyyy-MM-dd, PKT). Empty/missing = flag disabled.
        public static DateTime? FlagFromDate(IConfiguration config)
        {
            var raw = config == null ? null : config["Growth:FlagFromDate"];
            DateTime d;
            return !string.IsNullOrWhiteSpace(raw) && DateTime.TryParse(raw, out d) ? (DateTime?)d.Date : null;
        }
    }
}
