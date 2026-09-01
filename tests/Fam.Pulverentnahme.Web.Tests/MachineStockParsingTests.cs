using System.Xml.Linq;
using Fam.Pulverentnahme.Web;
using Xunit;

namespace Fam.Pulverentnahme.Web.Tests;

public sealed class MachineStockParsingTests
{
    [Fact]
    public void ParsesCapturedWithStockFilterWithoutHardcodingKidn()
    {
        var xml = XDocument.Parse("""
            <PARM>
              <TREE SUBTREES="TRUE">
                <CHILDREN>
                  <TREE FORMAT="ICO user.png">
                    <KEY>
                      <KIDN>0000101510</KIDN>
                      <FIRM></FIRM>
                      <FLTY>B</FLTY>
                      <SET>mit Bestand</SET>
                      <TYPE>RADIO</TYPE>
                    </KEY>
                    <VASI>mit Bestand</VASI>
                  </TREE>
                </CHILDREN>
                <STOP />
              </TREE>
            </PARM>
            """);

        var filter = MachineStockService.ParseWithStockFilter(xml);

        Assert.Equal("0000101510", filter.Kidn);
        Assert.Equal("B", filter.FilterType);
        Assert.Equal("mit Bestand", filter.Set);
        Assert.Equal("RADIO", filter.Type);
    }

    [Fact]
    public void ParsesCapturedPositiveEos1StockRow()
    {
        var xml = XDocument.Parse("""
            <PARM>
              <TABLE>
                <ROW>
                  <KEY>
                    <LALAGO>EOS1</LALAGO>
                    <SSID>ANSA7882652640999226</SSID>
                    <CRFM>LLAGEP</CRFM>
                    <LAIDNR>RP.00010</LAIDNR>
                    <LAPONR>RP10WEB_20260901_085443</LAPONR>
                    <LAFIRM>103</LAFIRM>
                  </KEY>
                  <LLAGEP.LAIDNR>RP.00010              RP.00010</LLAGEP.LAIDNR>
                  <IDNR.TLBEZG>AlSi10Mg</IDNR.TLBEZG>
                  <LLAGEP.LAPONR>RP10WEB_20260901_085443</LLAGEP.LAPONR>
                  <PONR.POCHNL></PONR.POCHNL>
                  <LLAWEP.LALABE>164,330 KGM</LLAWEP.LALABE>
                  <LLAWEP.LALADM>4773,80 EUR</LLAWEP.LALADM>
                  <LLAWEP.LAYZLBU>2026-09-01-14.15.42.401000</LLAWEP.LAYZLBU>
                </ROW>
                <STOP />
              </TABLE>
            </PARM>
            """);

        var row = Assert.Single(MachineStockService.ParseRows(xml));

        Assert.Equal("EOS1", row.Warehouse);
        Assert.Equal("RP.00010", row.Article);
        Assert.Equal("AlSi10Mg", row.ArticleText);
        Assert.Equal("RP10WEB_20260901_085443", row.Batch);
        Assert.Equal(164.330m, row.QuantityKg);
        Assert.Equal("KGM", row.Unit);
        Assert.Equal("2026-09-01-14.15.42.401000", row.LastBookingTimestamp);
    }

    [Theory]
    [InlineData("164,330 KGM", "164.330", "KGM")]
    [InlineData("0,000 KGM", "0", "KGM")]
    [InlineData("1.234,567 KGM", "1234.567", "KGM")]
    public void ParsesOxaionStockQuantity(string input, string expected, string unit)
    {
        var parsed = MachineStockService.ParseQuantity(input);
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), parsed.Quantity);
        Assert.Equal(unit, parsed.Unit);
    }

    [Fact]
    public void RejectsMissingWithStockFilter()
    {
        var xml = XDocument.Parse("<PARM><TREE><CHILDREN /></TREE></PARM>");
        Assert.Throws<InvalidOperationException>(() => MachineStockService.ParseWithStockFilter(xml));
    }
}
