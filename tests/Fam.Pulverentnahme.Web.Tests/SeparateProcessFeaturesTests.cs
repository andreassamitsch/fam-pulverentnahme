using System.Xml.Linq;
using Fam.Pulverentnahme.Web;
using Xunit;

namespace Fam.Pulverentnahme.Web.Tests;

public sealed class SeparateProcessFeaturesTests
{
    [Fact]
    public void FaMaterialListFindsExactPowderPosition()
    {
        var xml = XDocument.Parse("""
<ROOT><ROW><KEY><AMFAUN>FA25FK00001</AMFAUN><AMPOSN>10</AMPOSN><AMIDNK>RP.00010</AMIDNK></KEY><AMIDNK_TLST.TLBEZG>AlSi10Mg</AMIDNK_TLST.TLBEZG></ROW><STOP/></ROOT>
""");
        var rows = FaMaterialService.ParseMaterialList(xml, "FA25FK00001", "RP.00010");
        var row = Assert.Single(rows);
        Assert.Equal(10, row.Position);
        Assert.Equal("RP.00010", row.Article);
        Assert.Equal("AlSi10Mg", row.ArticleText);
    }

    [Fact]
    public void FaMaterialReadUsesRealConsumedFieldAndStatus()
    {
        var xml = XDocument.Parse("""
<ROOT><DTA><AMFAUN>FA25FK00001</AMFAUN><AMPOSN>10</AMPOSN><AMIDNK>RP.00010</AMIDNK><TX_IDNK02>AlSi10Mg</TX_IDNK02><AMMATB>15,410</AMMATB><AMMATV>15,420</AMMATV><AMMEKZ>KGM</AMMEKZ><AMMPST>9</AMMPST><TX_MPST>Komplett abgebucht</TX_MPST></DTA></ROOT>
""");
        var result = FaMaterialService.ParseMaterialRead(xml, "FA25FK00001", 10, "RP.00010", "");
        Assert.Equal(15.410m, result.RequiredKg);
        Assert.Equal(15.420m, result.ConsumedKg);
        Assert.Equal(9, result.MaterialStatus);
        Assert.False(result.MkBookingAllowed);
        Assert.Equal("Komplett abgebucht", result.MaterialStatusText);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(8, true)]
    [InlineData(9, false)]
    public void MkStatusGateMatchesConfirmedOxaionRule(int status, bool allowed) =>
        Assert.Equal(allowed, FaMaterialService.MkStatusAllowed(status));

    [Fact]
    public void AdditionalConsumptionBuildsNewActualTotal() =>
        Assert.Equal(15.430m, FaMaterialService.TargetConsumed(15.420m, 0.010m));

    [Theory]
    [InlineData(10, 15.420, 9, 10, 15.420, true)]
    [InlineData(10, 15.419, 9, 10, 15.420, false)]
    [InlineData(10, 15.420, 0, 10, 15.420, false)]
    [InlineData(20, 15.420, 9, 10, 15.420, false)]
    public void FaConsumptionSuccessRequiresExactPositionQuantityAndStatus(
        int actualPosition, double actualConsumed, int actualStatus,
        int expectedPosition, double expectedConsumed, bool expected)
    {
        var row = new FaMaterialPositionResult(
            "FA25FK00001", actualPosition, "RP.00010", "AlSi10Mg", 15.410m,
            (decimal)actualConsumed, "KGM", actualStatus,
            actualStatus == 9 ? "Komplett abgebucht" : "Eingeplant / Reserviert",
            FaMaterialService.MkStatusAllowed(actualStatus), DateTimeOffset.UtcNow);

        Assert.Equal(expected, FaConsumptionService.IsExactMkResult(row, expectedPosition, (decimal)expectedConsumed));
    }

    [Fact]
    public void TankOutVerificationRequiresExactLfLePair()
    {
        var spec = new TransferSpec(1, "LF", "RP.00010", "AlSi10Mg", "EOS1", "EOS 1 -Tank", "", "MIX1",
            "FAMLAB", "FAM Labor", "RE1F1", "", 149.574m);
        MovementRow[] rows =
        [
            new("1", "LF", "RP.00010", "MIX1", "EOS1", "", 149.574m, "t1"),
            new("1", "LE", "RP.00010", "MIX1", "FAMLAB", "RE1F1", 149.574m, "t1")
        ];
        Assert.True(MaterialTransferBookingService.MovementsComplete([spec], rows, out _));
    }

    [Fact]
    public void LmLnVerificationCreatesNewTargetMix()
    {
        var spec = new TransferSpec(2, "LM", "RP.00010", "AlSi10Mg", "EOS1", "EOS 1 -Tank", "", "RP00010MIX_OLD",
            "EOS1", "EOS 1 -Tank", "", "RP00010MIX_NEW", 20m, new DateOnly(2026, 9, 8));
        MovementRow[] rows =
        [
            new("2", "LM", "RP.00010", "RP00010MIX_OLD", "EOS1", "", 20m, "t1"),
            new("2", "LN", "RP.00010", "RP00010MIX_NEW", "EOS1", "", 20m, "t1")
        ];
        Assert.True(MaterialTransferBookingService.MovementsComplete([spec], rows, out _));
    }
}
