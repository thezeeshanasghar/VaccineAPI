using System;
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;

namespace VaccineAPI.Services
{
    // Reads featureflags with a 60 s cache. Any failure (table missing, DB hiccup) means OFF, so a flag can
    // never be the reason a request fails. Flags default off; turning one on is an UPDATE you run by hand.
    public static class FeatureFlags
    {
        private static readonly ConcurrentDictionary<string, (bool On, DateTime At)> Cache = new();
        public static TimeSpan Ttl = TimeSpan.FromSeconds(60);

        public static bool IsOn(Context db, string key)
        {
            if (Cache.TryGetValue(key, out var c) && DateTime.UtcNow - c.At < Ttl) return c.On;
            bool on;
            try { on = db.FeatureFlags.AsNoTracking().Where(f => f.FlagKey == key).Select(f => f.IsOn).FirstOrDefault(); }
            catch { on = false; }
            Cache[key] = (on, DateTime.UtcNow);
            return on;
        }

        public static void ResetCache() => Cache.Clear();
    }
}
