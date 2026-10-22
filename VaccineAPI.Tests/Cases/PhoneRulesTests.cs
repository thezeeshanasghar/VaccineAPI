using Xunit;

namespace VaccineAPI.Tests.Cases;

/// <summary>Per-country mobile length / prefix rules and the trunk-0 / country-code stripping.</summary>
public class PhoneRulesTests
{
    [Theory]
    [InlineData("92", "3001234567", "3001234567")]
    [InlineData("92", "03001234567", "3001234567")]
    [InlineData("92", "+92 300 1234567", "3001234567")]
    [InlineData("92", "0092-300-1234567", "3001234567")]
    [InlineData("966", "0512345678", "512345678")]
    [InlineData("1", "2025550123", "2025550123")]
    [InlineData("1-684", "2025550123", "2025550123")]
    [InlineData("44", "07911123456", "7911123456")]
    public void Valid_Numbers_Normalize(string cc, string raw, string expected)
    {
        Assert.Null(PhoneRules.Check(cc, raw, out var n));
        Assert.Equal(expected, n);
    }

    [Theory]
    [InlineData("92", "300123456")]      // 9 digits
    [InlineData("92", "30012345678")]    // 11 digits
    [InlineData("92", "2001234567")]     // must start with 3
    [InlineData("92", "042111222")]      // landline
    [InlineData("971", "412345678")]     // must start with 5
    [InlineData("92", "30012A4567")]
    [InlineData("92", "")]
    [InlineData("974", "1234567")]       // 7, needs 8
    [InlineData("999", "12345")]         // fallback 6-15
    public void Invalid_Numbers_Rejected(string cc, string raw)
    {
        Assert.NotNull(PhoneRules.Check(cc, raw, out _));
    }

    [Fact]
    public void Unknown_Country_Uses_E164_Range()
    {
        Assert.Null(PhoneRules.Check("999", "123456", out _));
        Assert.Null(PhoneRules.Check("999", "123456789012345", out _));
        Assert.NotNull(PhoneRules.Check("999", "1234567890123456", out _));
    }
}
