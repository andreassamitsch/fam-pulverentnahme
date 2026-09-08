using System.Xml.Linq;
using Fam.Pulverentnahme.Web;
using Xunit;

namespace Fam.Pulverentnahme.Web.Tests;

public sealed class InventoryServiceTests
{
    [Fact]
    public void BuildsConfirmedRpArticleSelectionForChargenJeFirma()
    {
        var fields = InventoryService.BuildRpArticleSelection("ANSA123");

        Assert.Equal("ANSA123", fields["SSID"]);
        Assert.Equal("IDNR.TLIDNR", fields["NAME"]);
        Assert.Equal("RP.*", fields["V_TLIDNR"]);
        Assert.Equal("", fields["B_TLIDNR"]);
    }

    [Fact]
    public void ParsesFilteredCompanyBatchIndexToConcreteNonZeroRpArticles()
    {
        var xml = XDocument.Parse("""
            <PARM><TABLE>
              <ROW><KEY><POIDNR>RP.00010</POIDNR></KEY><IDNR.TLIDNR>RP.00010</IDNR.TLIDNR><IDNR.TLBEZG>AlSi10Mg</IDNR.TLBEZG><UPOWEP.POLABE>1024,389 KGM</UPOWEP.POLABE><_CALC.W_LAGO>FAMLAB, H04KDX</_CALC.W_LAGO></ROW>
              <ROW><KEY><POIDNR>RP.00010</POIDNR></KEY><IDNR.TLIDNR>RP.00010</IDNR.TLIDNR><IDNR.TLBEZG>AlSi10Mg</IDNR.TLBEZG><UPOWEP.POLABE>149,574 KGM</UPOWEP.POLABE><_CALC.W_LAGO>EOS1</_CALC.W_LAGO></ROW>
              <ROW><KEY><POIDNR>RP.00012</POIDNR></KEY><IDNR.TLIDNR>RP.00012</IDNR.TLIDNR><IDNR.TLBEZG>316L</IDNR.TLBEZG><UPOWEP.POLABE>-0,250 KGM</UPOWEP.POLABE></ROW>
              <STOP />
            </TABLE></PARM>
            """);

        InventoryService.ValidateFilteredRpArticleIndex(xml);
        var rows = InventoryService.ParseRpArticleIndex(xml);

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.Article == "RP.00010" && r.ArticleText == "AlSi10Mg");
        Assert.Contains(rows, r => r.Article == "RP.00012" && r.ArticleText == "316L");
    }

    [Fact]
    public void RejectsCompanyBatchIndexWhenRpFilterDidNotTakeEffect()
    {
        var xml = XDocument.Parse("""
            <PARM><TABLE>
              <ROW><KEY><POIDNR>RP.00010</POIDNR></KEY><UPOWEP.POLABE>1,000 KGM</UPOWEP.POLABE></ROW>
              <ROW><KEY><POIDNR>VK.00001</POIDNR></KEY><UPOWEP.POLABE>1,000 KGM</UPOWEP.POLABE></ROW>
              <STOP />
            </TABLE></PARM>
            """);

        Assert.Throws<InvalidOperationException>(() => InventoryService.ValidateFilteredRpArticleIndex(xml));
    }

    [Fact]
    public void RejectsCompanyBatchIndexWhenStockFilterDidNotTakeEffect()
    {
        var xml = XDocument.Parse("""
            <PARM><TABLE>
              <ROW><KEY><POIDNR>RP.00010</POIDNR></KEY><UPOWEP.POLABE>0,000 KGM</UPOWEP.POLABE></ROW>
              <STOP />
            </TABLE></PARM>
            """);

        Assert.Throws<InvalidOperationException>(() => InventoryService.ValidateFilteredRpArticleIndex(xml));
    }

    [Fact]
    public void RejectsCompanyBatchIndexWithoutStop()
    {
        var xml = XDocument.Parse("""
            <PARM><TABLE>
              <ROW><KEY><POIDNR>RP.00010</POIDNR></KEY><UPOWEP.POLABE>1,000 KGM</UPOWEP.POLABE></ROW>
            </TABLE></PARM>
            """);

        Assert.Throws<InvalidOperationException>(() => InventoryService.ValidateFilteredRpArticleIndex(xml));
    }

    [Fact]
    public void ToleratesOnlyKnownSaveAllSelectionXmlParseFailure()
    {
        Assert.True(InventoryService.IsToleratedSaveAllSelectionNonXmlResponse(
            new InvalidOperationException("Oxaion response was not valid XML.")));
        Assert.False(InventoryService.IsToleratedSaveAllSelectionNonXmlResponse(
            new InvalidOperationException("other")));
    }

    [Fact]
    public void ParsesMultipleRpArticlesFromOneWarehouseBinList()
    {
        var xml = XDocument.Parse("""
            <PARM><TABLE>
              <ROW>
                <KEY><LPLAGO>H04HRL</LPLAGO><LPIDNR>RP.00010</LPIDNR><LPLAPL>RE1F3</LPLAPL><LPPONR>84671</LPPONR></KEY>
                <IDNR.TLBEZG>AlSi10Mg</IDNR.TLBEZG>
                <LLPWEP.LPLABE>1024,713 KGM</LLPWEP.LPLABE>
              </ROW>
              <ROW>
                <KEY><LPLAGO>H04HRL</LPLAGO><LPIDNR>RP.00012</LPIDNR><LPLAPL>RE2F1</LPLAPL><LPPONR>MIX12</LPPONR></KEY>
                <IDNR.TLBEZG>316L</IDNR.TLBEZG>
                <LLPWEP.LPLABE>-0,250 KGM</LLPWEP.LPLABE>
              </ROW>
              <ROW>
                <KEY><LPLAGO>H04HRL</LPLAGO><LPIDNR>VK.00001</LPIDNR><LPLAPL>RE9F9</LPLAPL><LPPONR>X</LPPONR></KEY>
                <LLPWEP.LPLABE>5,000 KGM</LLPWEP.LPLABE>
              </ROW>
              <STOP />
            </TABLE></PARM>
            """);

        var rows = InventoryService.ParseInventoryBinRows(xml, "Halle 04 Hochregallager");

        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, r => r.Article == "RP.00010" && r.StorageBin == "RE1F3" && r.Batch == "84671" && r.QuantityKg == 1024.713m);
        Assert.Contains(rows, r => r.Article == "RP.00012" && r.StorageBin == "RE2F1" && r.QuantityKg == -0.250m);
        Assert.Contains(rows, r => r.Article == "VK.00001" && r.QuantityKg == 5m);
    }

    [Fact]
    public void UsesInternalStorageBinKeyFromOxaionKey()
    {
        var xml = XDocument.Parse("""
            <PARM><TABLE><ROW>
              <KEY><LPLAGO>H04HRL</LPLAGO><LPIDNR>RP.00010</LPIDNR><LPLAPL>RE1F3</LPLAPL><LPPONR>84671</LPPONR></KEY>
              <LLPWEP.LPLAPL>RE1  F 3</LLPWEP.LPLAPL>
              <LLPWEP.LPLABE>1,000 KGM</LLPWEP.LPLABE>
            </ROW><STOP /></TABLE></PARM>
            """);

        var row = Assert.Single(InventoryService.ParseInventoryBinRows(xml, "H04HRL"));
        Assert.Equal("RE1F3", row.StorageBin);
    }
}
