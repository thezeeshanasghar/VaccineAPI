using System;
using System.Security.Cryptography;
using System.Text;

namespace VaccineAPI
{
    // Stateless, signed magic-link tokens for a PA-assignment email (VacDoc).
    //
    // Same HMAC-SHA256 scheme as LinkLoginToken (parent/VacParent), kept as a
    // separate class rather than a shared/generic one so the parent login path
    // is never touched by PA-side changes. A token carries the PA's UserId, the
    // PAAssignment's Id, and an absolute expiry. No DB row is stored.
    //
    // Wire format (all URL-safe base64, no padding):
    //     <payloadB64>.<signatureB64>
    // where payload = "userId:paAssignmentId:expiryUnixSeconds"
    public static class PaAssignmentLinkToken
    {
        // How long a freshly generated link stays usable — a rolling 24 hours
        // from send time, not a calendar-day cutoff.
        public const int ValidHours = 24;

        public static string Generate(long userId, long paAssignmentId, string secret)
        {
            long expiry = DateTimeOffset.UtcNow.AddHours(ValidHours).ToUnixTimeSeconds();
            string payload = userId + ":" + paAssignmentId + ":" + expiry;
            string payloadB64 = UrlSafe(Encoding.UTF8.GetBytes(payload));
            string sig = UrlSafe(Sign(payloadB64, secret));
            return payloadB64 + "." + sig;
        }

        // Returns true and sets userId/paAssignmentId when the token is well-formed,
        // correctly signed, and not expired. Returns false otherwise.
        public static bool Validate(string token, string secret, out long userId, out long paAssignmentId)
        {
            userId = 0;
            paAssignmentId = 0;

            if (string.IsNullOrWhiteSpace(token))
                return false;

            var parts = token.Split('.');
            if (parts.Length != 2)
                return false;

            string payloadB64 = parts[0];
            string providedSig = parts[1];

            string expectedSig = UrlSafe(Sign(payloadB64, secret));
            if (!FixedTimeEquals(providedSig, expectedSig))
                return false;

            string payload;
            try
            {
                payload = Encoding.UTF8.GetString(FromUrlSafe(payloadB64));
            }
            catch
            {
                return false;
            }

            var fields = payload.Split(':');
            if (fields.Length != 3)
                return false;

            if (!long.TryParse(fields[0], out userId)
                || !long.TryParse(fields[1], out paAssignmentId)
                || !long.TryParse(fields[2], out long expiry))
            {
                userId = 0;
                paAssignmentId = 0;
                return false;
            }

            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expiry)
            {
                userId = 0;
                paAssignmentId = 0;
                return false;
            }

            return true;
        }

        private static byte[] Sign(string data, string secret)
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret ?? "")))
            {
                return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
            }
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private static string UrlSafe(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static byte[] FromUrlSafe(string s)
        {
            string b64 = s.Replace('-', '+').Replace('_', '/');
            switch (b64.Length % 4)
            {
                case 2: b64 += "=="; break;
                case 3: b64 += "="; break;
            }
            return Convert.FromBase64String(b64);
        }
    }
}
