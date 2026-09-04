using Fam.Pulverentnahme.Web;
using Xunit;

namespace Fam.Pulverentnahme.Web.Tests;

public sealed class RejectedScanEventTests
{
    [Fact]
    public void NormalizesSupportedReasonAndFields()
    {
        var result = RejectedScanEventStore.Normalize(new RejectedChargeScanRequest(
            " wrong_article ",
            " EOS2 ",
            " RP.00012 ",
            " RP.00010 ",
            " 87911 "));

        Assert.Equal("WRONG_ARTICLE", result.Reason);
        Assert.Equal("EOS2", result.MachineWarehouse);
        Assert.Equal("RP.00012", result.ExpectedArticle);
        Assert.Equal("RP.00010", result.ScannedArticle);
        Assert.Equal("87911", result.ScannedBatch);
    }

    [Theory]
    [InlineData("WRONG_ARTICLE")]
    [InlineData("CHARGE_NOT_FOUND")]
    [InlineData("SOURCE_ALREADY_USED")]
    [InlineData("INVALID_QR_FORMAT")]
    public void AcceptsKnownRejectedScanReasons(string reason)
    {
        var result = RejectedScanEventStore.Normalize(new RejectedChargeScanRequest(reason, "EOS2", "RP.00012", "", ""));
        Assert.Equal(reason, result.Reason);
    }

    [Fact]
    public void RejectsUnknownReason()
    {
        Assert.Throws<ArgumentException>(() => RejectedScanEventStore.Normalize(
            new RejectedChargeScanRequest("SOMETHING_ELSE", "EOS2", "RP.00012", "RP.00010", "87911")));
    }

    [Fact]
    public void RejectsControlCharacters()
    {
        Assert.Throws<ArgumentException>(() => RejectedScanEventStore.Normalize(
            new RejectedChargeScanRequest("WRONG_ARTICLE", "EOS2", "RP.00012", "RP.00010\nX", "87911")));
    }
}
