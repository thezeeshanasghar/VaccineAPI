using Newtonsoft.Json;
using Xunit;

namespace VaccineAPI.Tests.Cases
{
    public class OnlyDateConverterTests
    {
        private class Holder
        {
            [JsonConverter(typeof(OnlyDateConverter))]
            public System.DateTime Date { get; set; }
        }

        [Theory]
        [InlineData("22-10-2026")]
        [InlineData("2026-10-22")]
        [InlineData("2026-10-22T00:00:00")]
        public void Accepts_legacy_canonical_and_iso_days(string input)
        {
            var h = JsonConvert.DeserializeObject<Holder>("{\"Date\":\"" + input + "\"}");
            Assert.Equal(new System.DateTime(2026, 10, 22), h.Date.Date);
        }
        private class NullableHolder
        {
            [JsonConverter(typeof(OnlyDateConverter))]
            public System.DateTime? GivenDate { get; set; }
        }

        // TIMESTAMP_SPEC 11.5.1: a Day never keeps a time. A UTC instant is read as its PKT day
        // (pre-10-21 clients sent PKT midnight as "...T19:00:00Z" of the previous UTC day).
        [Theory]
        [InlineData("2026-11-25T19:00:00Z", 2026, 11, 26)]
        [InlineData("2026-11-25T19:00:00.000Z", 2026, 11, 26)]
        [InlineData("2026-11-25T18:59:59Z", 2026, 11, 25)]
        [InlineData("2026-11-25T17:00:00+07:00", 2026, 11, 25)]
        [InlineData("2026-11-25T10:30:00", 2026, 11, 25)]
        [InlineData("25-11-2026", 2026, 11, 25)]
        public void Day_never_keeps_a_time(string input, int y, int m, int d)
        {
            var h = JsonConvert.DeserializeObject<Holder>("{\"Date\":\"" + input + "\"}");
            Assert.Equal(new System.DateTime(y, m, d), h!.Date);
            Assert.Equal(System.TimeSpan.Zero, h.Date.TimeOfDay);
            var n = JsonConvert.DeserializeObject<NullableHolder>("{\"GivenDate\":\"" + input + "\"}");
            Assert.Equal(new System.DateTime(y, m, d), n!.GivenDate);
        }

        [Fact]
        public void Null_given_date_stays_null()
        {
            var n = JsonConvert.DeserializeObject<NullableHolder>("{\"GivenDate\":null}");
            Assert.Null(n!.GivenDate);
        }
    }
}