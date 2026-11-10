using System;
using VaccineAPI;
using Xunit;

public class VerifyThrottleTests
{
    [Fact]
    public void Three_misses_suspend_for_15_minutes_and_other_ips_are_unaffected()
    {
        VerifyThrottle.Reset();
        VerifyThrottle.Fail("1.1.1.1");
        VerifyThrottle.Fail("1.1.1.1");
        Assert.False(VerifyThrottle.TryGetBlock("1.1.1.1", out _));

        VerifyThrottle.Fail("1.1.1.1");
        Assert.True(VerifyThrottle.TryGetBlock("1.1.1.1", out var left));
        Assert.InRange(left.TotalMinutes, 14.9, 15.0);
        Assert.False(VerifyThrottle.TryGetBlock("2.2.2.2", out _));
    }

    [Fact]
    public void Misses_during_a_suspension_do_not_stack_the_next_step()
    {
        VerifyThrottle.Reset();
        for (int i = 0; i < 10; i++) VerifyThrottle.Fail("3.3.3.3");
        Assert.True(VerifyThrottle.TryGetBlock("3.3.3.3", out var left));
        Assert.True(left.TotalMinutes <= 15.0);
    }

    [Theory]
    [InlineData("GET", "/api/child/VerifyRecord", true)]
    [InlineData("GET", "/api/child/123/verification-Schedule-PDF", true)]
    [InlineData("GET", "/api/child/123/ScheduleVerify", true)]
    [InlineData("POST", "/api/user/verify", false)]
    [InlineData("GET", "/api/child/123", false)]
    public void Only_anonymous_verify_gets_are_throttled(string method, string path, bool expected) =>
        Assert.Equal(expected, VerifyThrottle.Applies(method, path));
}
