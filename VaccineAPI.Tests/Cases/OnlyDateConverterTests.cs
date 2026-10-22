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
    }
}
