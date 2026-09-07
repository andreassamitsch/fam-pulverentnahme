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
    public void EmptyTankFillVerificationAllowsMixChargeAsSourceAndCreatesNewTargetMix()
    {
        var spec = new TransferSpec(1, "LM", "RP.00010", "AlSi10Mg", "FAMLAB", "FAM Labor", "RE1F1", "RP00010MIX_OLD",
            "EOS1", "EOS 1 -Tank", "", "RP00010MIX_NEW", 20m, new DateOnly(2026, 9, 7));
        MovementRow[] rows =
        [
            new("1", "LM", "RP.00010", "RP00010MIX_OLD", "FAMLAB", "RE1F1", 20m, "t1"),
            new("1", "LN", "RP.00010", "RP00010MIX_NEW", "EOS1", "", 20m, "t1")
        ];
        Assert.True(MaterialTransferBookingService.MovementsComplete([spec], rows, out _));
    }

    [Fact]
    public void InventoryCompanyBatchIndexKeepsOnlyDistinctNonZeroRpArticles()
    {
        var xml = XDocument.Parse("""
<ROOT>
  <ROW><KEY><POIDNR>RP.00002</POIDNR><POPONR>72911</POPONR></KEY><IDNR.TLBEZG>PureCu</IDNR.TLBEZG><UPOWEP.POLABE>22,446</UPOWEP.POLABE><_CALC.W_LAGO>FAMLAB, H04KDX</_CALC.W_LAGO></ROW>
  <ROW><KEY><POIDNR>RP.00002</POIDNR><POPONR>72912</POPONR></KEY><IDNR.TLBEZG>PureCu</IDNR.TLBEZG><UPOWEP.POLABE>5,000</UPOWEP.POLABE><_CALC.W_LAGO>EOS1</_CALC.W_LAGO></ROW>
  <ROW><KEY><POIDNR>RP.00003</POIDNR><POPONR>TESTNEG</POPONR></KEY><IDNR.TLBEZG>Ti64</IDNR.TLBEZG><UPOWEP.POLABE>-0,250</UPOWEP.POLABE></ROW>
  <ROW><KEY><POIDNR>RP.00004</POIDNR><POPONR>ZERO</POPONR></KEY><IDNR.TLBEZG>Zero</IDNR.TLBEZG><UPOWEP.POLABE>0,000</UPOWEP.POLABE></ROW>
  <ROW><KEY><POIDNR>XX.00001</POIDNR><POPONR>OTHER</POPONR></KEY><IDNR.TLBEZG>Other</IDNR.TLBEZG><UPOWEP.POLABE>99,000</UPOWEP.POLABE></ROW>
  <STOP/>
</ROOT>
""");

        var rows = InventoryService.ParseRpArticleIndex(xml);

        Assert.Equal(2, rows.Count);
        Assert.Equal("RP.00002", rows[0].Article);
        Assert.Equal("PureCu", rows[0].ArticleText);
        Assert.Equal("RP.00003", rows[1].Article);
    }

    [Fact]
    public void InventoryCompanyBatchIndexDoesNotUseAggregatedWarehouseDisplay()
    {
        var xml = XDocument.Parse("""
<ROOT><ROW><KEY><POIDNR>RP.00002</POIDNR></KEY><IDNR.TLBEZG>PureCu</IDNR.TLBEZG><UPOWEP.POLABE>22,446</UPOWEP.POLABE><_CALC.W_LAGO>FAMLAB, H04KDX</_CALC.W_LAGO></ROW><STOP/></ROOT>
""");

        var row = Assert.Single(InventoryService.ParseRpArticleIndex(xml));
        Assert.Equal("RP.00002", row.Article);
        Assert.Equal("PureCu", row.ArticleText);
    }
}
