using Fam.Pulverentnahme.Web;
using Xunit;

namespace Fam.Pulverentnahme.Web.Tests;

public sealed class RfidPersonnelTests
{
    [Theory]
    [InlineData("03:3C:DD:52", "54320466")]
    [InlineData("03-3c-dd-52", "54320466")]
    [InlineData("54320466", "54320466")]
    public void ConvertsWebNfcSerialToSyncosRfid(string serialNumber, string expected)
    {
        Assert.Equal(expected, RfidPersonnelService.SerialNumberToRfid(serialNumber));
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
