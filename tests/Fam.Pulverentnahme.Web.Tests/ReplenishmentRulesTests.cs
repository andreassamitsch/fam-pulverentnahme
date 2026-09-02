using Fam.Pulverentnahme.Web;
using Xunit;

namespace Fam.Pulverentnahme.Web.Tests;

public sealed class ReplenishmentRulesTests
{
    [Fact]
    public void CreatesConfiguredMixBatchNameFromArticleAndTimestamp()
    {
        var value = ReplenishmentRules.CreateMixBatch("RP.00010", new DateTime(2026, 9, 2, 16, 23, 12));
        Assert.Equal("RP00010MIX_20260902_162312", value);
        Assert.True(ReplenishmentRules.IsValidGeneratedMixBatch("RP.00010", value, new DateOnly(2026, 9, 2)));
    }

    [Fact]
    public void BookingTextFollowsMachineWarehouse()
    {
        Assert.Equal("Pulver nachfüllen EOS2", ReplenishmentRules.BookingText("EOS2"));
    }

    [Fact]
    public void RejectsInvalidGeneratedMixTime()
    {
        Assert.False(ReplenishmentRules.IsValidGeneratedMixBatch("RP.00010", "RP00010MIX_20260902_256199", new DateOnly(2026, 9, 2)));
    }
}
