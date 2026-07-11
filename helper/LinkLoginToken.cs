using System;
using System.Security.Cryptography;
using System.Text;

namespace VaccineAPI
{
    // Stateless, signed magic-link tokens for the parent app (VacParent).
    //
    // A token carries the parent's UserId, the child's Id, and an absolute
    // expiry, signed with HMAC-SHA256 using a server-side secret. No DB row is
    // stored, so nothing to migrate and nothing to clean up. The link stays
    // valid until it expires (currently 7 days from issue).
    //
    // Wire format (all URL-safe base64, no padding):
    //     <payloadB64>.<signatureB64>
    // where payload = "userId:childId:expiryUnixSeconds"
    //
    // VacDoc embeds Generate(...) output into the WhatsApp reminder URL;
    // VacParent posts it back to /api/user/link-login which calls Validate(...).
    public static class LinkLoginToken
    {
        // How long a freshly generated link stays usable.
        public const int ValidDays = 7;

        public static string Generate(long userId, long childId, string secret)
        {
            long expiry = DateTimeOffset.UtcNow.AddDays(ValidDays).ToUnixTimeSeconds();
            string payload = userId + ":" + childId + ":" + expiry;
            string payloadB64 = UrlSafe(Encoding.UTF8.GetBytes(payload));
            string sig = UrlSafe(Sign(payloadB64, secret));
            return payloadB64 + "." + sig;
        }

        // Returns true and sets userId/childId when the token is well-formed,
        // correctly signed, and not expired. Returns false otherwise.
        public static bool Validate(string token, string secret, out long userId, out long childId)
        {
            userId = 0;
            childId = 0;

            if (string.IsNullOrWhiteSpace(token))
                return false;

            var parts = token.Split('.');
            if (parts.Length != 2)
                return false;

            string payloadB64 = parts[0];
            string providedSig = parts[1];

            // Constant-time-ish comparison of the recomputed signature.
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
                || !long.TryParse(fields[1], out childId)
                || !long.TryParse(fields[2], out long expiry))
            {
                userId = 0;
                childId = 0;
                return false;
            }

            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expiry)
            {
                userId = 0;
                childId = 0;
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
