using System.Xml.Linq;
using Fam.Pulverentnahme.Web;
using Xunit;

namespace Fam.Pulverentnahme.Web.Tests;

public sealed class MachineStockParsingTests
{
    [Fact]
    public void ParsesCapturedUnfilteredEos1RowsAcrossArticles()
    {
        var xml = XDocument.Parse("""
            <PARM>
              <TABLE>
                <HEADER NbrOfRows="3" />
                <ROW>
                  <KEY><LALAGO>EOS1</LALAGO><LAIDNR>RP.00010</LAIDNR><LAPONR>OLD_ZERO</LAPONR><LAFIRM>103</LAFIRM></KEY>
                  <IDNR.TLBEZG>AlSi10Mg</IDNR.TLBEZG>
                  <LLAWEP.LALABE>0,000 KGM</LLAWEP.LALABE>
                </ROW>
                <ROW>
                  <KEY><LALAGO>EOS1</LALAGO><LAIDNR>RP.00010</LAIDNR><LAPONR>RP10WEB_20260901_085443</LAPONR><LAFIRM>103</LAFIRM></KEY>
                  <IDNR.TLBEZG>AlSi10Mg</IDNR.TLBEZG>
                  <LLAWEP.LALABE>164,330 KGM</LLAWEP.LALABE>
                  <LLAWEP.LAYZLBU>2026-09-01-14.15.42.401000</LLAWEP.LAYZLBU>
                </ROW>
                <ROW>
                  <KEY><LALAGO>EOS1</LALAGO><LAIDNR>RP.00012</LAIDNR><LAPONR>RP12MIX</LAPONR><LAFIRM>103</LAFIRM></KEY>
                  <IDNR.TLBEZG>316L</IDNR.TLBEZG>
                  <LLAWEP.LALABE>0,000 KGM</LLAWEP.LALABE>
                </ROW>
                <STOP />
              </TABLE>
            </PARM>
            """);

        var rows = MachineStockService.ParseRows(xml);

        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, r => r.Article == "RP.00010" && r.Batch == "RP10WEB_20260901_085443" && r.QuantityKg == 164.330m);
        Assert.Contains(rows, r => r.Article == "RP.00012" && r.Batch == "RP12MIX" && r.QuantityKg == 0m);
        Assert.True(MachineStockService.HasStop(xml));
    }

    [Fact]
    public void ConfirmedNonZeroConditionDropsZeroButKeepsNegativeForSafetyHandling()
    {
        var xml = XDocument.Parse("""
            <PARM><TABLE>
              <ROW><KEY><LALAGO>EOS1</LALAGO><LAIDNR>RP.00010</LAIDNR><LAPONR>A</LAPONR></KEY><LLAWEP.LALABE>0,000 KGM</LLAWEP.LALABE></ROW>
              <ROW><KEY><LALAGO>EOS1</LALAGO><LAIDNR>RP.00010</LAIDNR><LAPONR>B</LAPONR></KEY><LLAWEP.LALABE>1,250 KGM</LLAWEP.LALABE></ROW>
              <ROW><KEY><LALAGO>EOS1</LALAGO><LAIDNR>RP.00012</LAIDNR><LAPONR>C</LAPONR></KEY><LLAWEP.LALABE>-0,500 KGM</LLAWEP.LALABE></ROW>
              <STOP />
            </TABLE></PARM>
            """);

        var nonZero = MachineStockService.ParseRows(xml).Where(r => r.QuantityKg != 0m).ToList();

        Assert.Equal(2, nonZero.Count);
        Assert.Contains(nonZero, r => r.Batch == "B" && r.QuantityKg == 1.250m);
        Assert.Contains(nonZero, r => r.Batch == "C" && r.QuantityKg == -0.500m);
    }

    [Theory]
    [InlineData("164,330 KGM", "164.330", "KGM")]
    [InlineData("0,000 KGM", "0", "KGM")]
    [InlineData("-0,500 KGM", "-0.500", "KGM")]
    [InlineData("1.234,567 KGM", "1234.567", "KGM")]
    public void ParsesOxaionStockQuantity(string input, string expected, string unit)
    {
        var parsed = MachineStockService.ParseQuantity(input);
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), parsed.Quantity);
        Assert.Equal(unit, parsed.Unit);
    }

    [Fact]
    public void DetectsMissingStopMarker()
    {
        var xml = XDocument.Parse("<PARM><TABLE><ROW /></TABLE></PARM>");
        Assert.False(MachineStockService.HasStop(xml));
    }
}
