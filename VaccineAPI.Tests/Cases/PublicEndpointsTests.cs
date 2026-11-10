using VaccineAPI;
using Xunit;

namespace VaccineAPI.Tests.Cases
{
    public class PublicEndpointsTests
    {
        [Theory]
        [InlineData("/api/Child/12/2026-11-09/2026-11-09/0/Verification-Invoice-PDF")]
        [InlineData("/api/Child/12/verification-Schedule-PDF")]
        [InlineData("/api/Child/12/verification-Custom-PDF")]
        [InlineData("/api/Child/VerifyRecord")]
        [InlineData("/api/Child/VerifyInvoice")]
        [InlineData("/api/Child/PIDVerify/5")]
        [InlineData("/api/Child/Travel-PDF-Download-Verification/5")]
        [InlineData("/api/Child/12/ScheduleVerify")]
        public void Printed_and_shared_verification_links_stay_public(string path)
            => Assert.True(PublicEndpoints.IsPublic("GET", path));

        [Theory]
        [InlineData("/api/Child/12/2026-11-09/2026-11-09/0/Verify-Invoice-PDF")]
        [InlineData("/api/InventoryBackfill/verify")]
        [InlineData("/api/Child/12/2026-11-09/0/Download-Invoice-PDF")]
        [InlineData("/api/Anything/verification-x")]
        public void Mutating_admin_and_other_routes_need_a_token(string path)
            => Assert.False(PublicEndpoints.IsPublic("GET", path));
    }
}
