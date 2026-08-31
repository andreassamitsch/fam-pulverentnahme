using System.Reflection;
using Fam.Pulverentnahme.Web;

namespace Fam.Pulverentnahme.Web.Tests;

public sealed class QuantityParsingTests
{
    private static decimal Parse(string value)
    {
        var method = typeof(MixBookingService).GetMethod("ParseEu", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("MixBookingService.ParseEu not found.");
        return (decimal)(method.Invoke(null, [value]) ?? throw new InvalidOperationException("ParseEu returned null."));
    }

    [Theory]
    [InlineData("0,001 KGM", "0.001")]
    [InlineData("0,005 KGM", "0.005")]
    [InlineData("0,001", "0.001")]
    [InlineData("1.234,567 KGM", "1234.567")]
    [InlineData("1,234.567 KGM", "1234.567")]
    [InlineData("0,000 KGM", "0")]
    public void ParsesOxaionMovementQuantity(string input, string expected)
    {
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), Parse(input));
    }

    [Fact]
    public void RejectsUnknownNonEmptyQuantityFormat()
    {
        var ex = Assert.Throws<TargetInvocationException>(() => Parse("KGM"));
        Assert.IsType<FormatException>(ex.InnerException);
    }
}
