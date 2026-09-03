using Fam.Pulverentnahme.Web;
using Xunit;

namespace Fam.Pulverentnahme.Web.Tests;

public sealed class RfidPersonnelTests
{
    [Theory]
    [InlineData("54:32:04:66", "54320466")]
    [InlineData("54-32-04-66", "54320466")]
    [InlineData("54 32 04 66", "54320466")]
    [InlineData("54320466", "54320466")]
    [InlineData("A1:B2:C3:D4", "A1B2C3D4")]
    [InlineData("a1-b2-c3-d4", "A1B2C3D4")]
    public void NormalizesWebNfcSerialToSyncosRfidString(string serialNumber, string expected)
    {
        Assert.Equal(expected, RfidPersonnelService.NormalizeSerialNumber(serialNumber));
    }

    [Fact]
    public void ConvertsSyncosObjectKeyToPersonnelNumber()
    {
        Assert.Equal("446", RfidPersonnelService.NormalizeObjectKey("0000000446"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABC")]
    [InlineData("44A6")]
    public void RejectsNonNumericObjectKey(string objectKey)
    {
        Assert.Throws<InvalidOperationException>(() => RfidPersonnelService.NormalizeObjectKey(objectKey));
    }
}
