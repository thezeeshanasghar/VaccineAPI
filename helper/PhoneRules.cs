using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace VaccineAPI
{
    // Mobile-number rules per country dial code. Mobile only, no landlines.
    // Keep in sync with VacDoc/src/app/shared/phone-rules.ts (see VacDoc/PHONE_VALIDATION_SPEC.md).
    public static class PhoneRules
    {
        private class Rule
        {
            public int Min, Max;
            public string Start;   // regex source the national number must begin with
            public bool Trunk0;    // country writes a leading 0 domestically; strip one
        }

        private static Rule R(int min, int max, string start = null, bool trunk0 = false) =>
            new Rule { Min = min, Max = max, Start = start, Trunk0 = trunk0 };

        private static readonly Dictionary<string, Rule> Rules = new Dictionary<string, Rule>
        {
            ["92"] = R(10, 10, "3", true),
            ["966"] = R(9, 9, "5", true),
            ["971"] = R(9, 9, "5", true),
            ["44"] = R(10, 10, "7", true),
            ["1"] = R(10, 10),
            ["974"] = R(8, 8),
            ["965"] = R(8, 8),
            ["968"] = R(8, 8),
            ["973"] = R(8, 8),
            ["60"] = R(9, 10, null, true),
            ["61"] = R(9, 9, "4", true),
            ["49"] = R(10, 11, null, true),
            ["39"] = R(9, 10),
            ["33"] = R(9, 9, null, true),
            ["34"] = R(9, 9),
            ["31"] = R(9, 9, null, true),
            ["47"] = R(8, 8),
            ["46"] = R(7, 10, null, true),
            ["45"] = R(8, 8),
            ["353"] = R(9, 9, null, true),
            ["32"] = R(9, 9, null, true),
            ["30"] = R(10, 10),
            ["90"] = R(10, 10, null, true),
            ["86"] = R(11, 11),
            ["81"] = R(10, 10, null, true),
            ["82"] = R(9, 10, null, true),
            ["852"] = R(8, 8),
            ["65"] = R(8, 8),
            ["93"] = R(9, 9, null, true),
            ["91"] = R(10, 10, null, true),
            ["880"] = R(10, 10, null, true),
            ["98"] = R(10, 10, null, true),
            ["27"] = R(9, 9, null, true),
            ["64"] = R(8, 10, null, true),
        };

        // Any country not in the table: E.164 range.
        private static readonly Rule Fallback = R(6, 15);

        // "1-684" style codes (NANP territories) share dial code 1; "+92" -> "92".
        public static string DialKey(string cc) =>
            new string(((cc ?? "").Split('-')[0]).Where(char.IsDigit).ToArray());

        private static Rule RuleFor(string cc) =>
            Rules.TryGetValue(DialKey(cc), out var r) ? r : Fallback;

        // Strip spaces/dashes, +/00 prefix, a typed country code and the trunk 0.
        public static string Normalize(string cc, string raw)
        {
            var key = DialKey(cc);
            var rule = RuleFor(cc);
            var v = Regex.Replace(raw ?? "", @"[\s\-().]", "");
            bool hadPrefix = false;
            if (v.StartsWith("+")) { v = v.Substring(1); hadPrefix = true; }
            else if (v.StartsWith("00")) { v = v.Substring(2); hadPrefix = true; }
            if (key.Length > 0 && v.StartsWith(key) && (hadPrefix || v.Length > rule.Max))
                v = v.Substring(key.Length);
            if (rule.Trunk0 && v.StartsWith("0"))
                v = v.Substring(1);
            return v;
        }

        // Returns null when valid; otherwise the message. `normalized` is the value to store.
        public static string Check(string cc, string raw, out string normalized)
        {
            normalized = Normalize(cc, raw);
            if (normalized.Length == 0) return "Mobile number is required.";
            var rule = RuleFor(cc);
            var key = DialKey(cc);
            if (!normalized.All(char.IsDigit)) return "Mobile number must contain digits only.";
            if (normalized.Length < rule.Min || normalized.Length > rule.Max)
            {
                var len = rule.Min == rule.Max ? "exactly " + rule.Min : rule.Min + " to " + rule.Max;
                return "Mobile number for +" + key + " must be " + len + " digits" +
                       (key == "92" ? " (e.g. 3001234567)." : ".");
            }
            if (rule.Start != null && !Regex.IsMatch(normalized, "^" + rule.Start))
                return key == "92"
                    ? "Pakistani mobile numbers start with 3 (e.g. 3001234567)."
                    : "Mobile number for +" + key + " is not a valid mobile number.";
            return null;
        }
    }
}
