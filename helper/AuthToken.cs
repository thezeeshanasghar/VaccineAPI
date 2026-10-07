using System;
using System.Security.Cryptography;
using System.Text;

namespace VaccineAPI
{
    // Stateless, signed session token issued at login and sent by every app as
    // "Authorization: Bearer <token>". Nothing is stored in the database.
    //
    // Wire format (URL-safe base64, no padding):  <payloadB64>.<signatureB64>
    // payload = "kind:id:issuedAt:expiry:stampTag"
    //   kind     "U" = row in Users (doctor, PA, manager, parent, super admin)
    //            "A" = row in Agents
    //   stampTag short hash of the account's SecurityStamp (agents: of the password),
    //            so changing the password revokes every token issued before it
    //            without the token ever carrying the stamp itself.
    public static class AuthToken
    {
        public const int ValidDays = 60;
        // Past this age the API hands back a fresh token in the X-Auth-Token header.
        public const int RefreshAfterDays = 30;

        public static string Issue(char kind, long id, string stampSource, string secret, DateTimeOffset? now = null)
        {
            var t = now ?? DateTimeOffset.UtcNow;
            string payload = kind + ":" + id + ":" + t.ToUnixTimeSeconds() + ":" + t.AddDays(ValidDays).ToUnixTimeSeconds() + ":" + StampTag(stampSource);
            string payloadB64 = UrlSafe(Encoding.UTF8.GetBytes(payload));
            return payloadB64 + "." + UrlSafe(Sign(payloadB64, secret));
        }

        public static bool TryValidate(string? token, string secret, out char kind, out long id, out string stampTag, out long issuedAt)
        {
            kind = default; id = 0; stampTag = ""; issuedAt = 0;
            if (string.IsNullOrWhiteSpace(token) || token.Length > 400) return false;

            var parts = token.Split('.');
            if (parts.Length != 2) return false;

            if (!FixedTimeEquals(parts[1], UrlSafe(Sign(parts[0], secret)))) return false;

            string payload;
            try { payload = Encoding.UTF8.GetString(FromUrlSafe(parts[0])); }
            catch { return false; }

            var f = payload.Split(':');
            if (f.Length != 5 || f[0].Length != 1) return false;
            if (!long.TryParse(f[1], out id) || !long.TryParse(f[2], out issuedAt) || !long.TryParse(f[3], out long expiry))
                return false;
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expiry) return false;

            kind = f[0][0];
            if (kind != 'U' && kind != 'A') return false;
            stampTag = f[4];
            return true;
        }

        public static string StampTag(string? source)
        {
            using var sha = SHA256.Create();
            return UrlSafe(sha.ComputeHash(Encoding.UTF8.GetBytes(source ?? ""))).Substring(0, 16);
        }

        private static byte[] Sign(string data, string secret)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret ?? ""));
            return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private static string UrlSafe(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static byte[] FromUrlSafe(string s)
        {
            string b64 = s.Replace('-', '+').Replace('_', '/');
            switch (b64.Length % 4) { case 2: b64 += "=="; break; case 3: b64 += "="; break; }
            return Convert.FromBase64String(b64);
        }
    }
}
