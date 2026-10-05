using System.Xml.Linq;
using Fam.Pulverentnahme.Web;
using Xunit;

namespace Fam.Pulverentnahme.Web.Tests;

public sealed class ArticleRecognitionColorsTests
{
    [Fact]
    public void Parse_ReadsConfirmedEfaColorsFromOxaionRows()
    {
        var xml = XDocument.Parse("""
            <PARM><TABLE>
              <ROW>
                <KEY><ASSMMN>EFA01</ASSMMN></KEY>
                <UYASMP.ASSMMN>EFA01</UYASMP.ASSMMN>
                <_INTERN.SMMNBZ>Erkennungs Farbe 1</_INTERN.SMMNBZ>
                <UYASMP.ASSMMA>0D0D0D</UYASMP.ASSMMA>
                <_INTERN.SMMABZ>Schwarz</_INTERN.SMMABZ>
              </ROW>
              <ROW>
                <KEY><ASSMMN>EFA02</ASSMMN></KEY>
                <UYASMP.ASSMMN>EFA02</UYASMP.ASSMMN>
                <_INTERN.SMMNBZ>Erkennungs Farbe 2</_INTERN.SMMNBZ>
                <UYASMP.ASSMMA>7030A0</UYASMP.ASSMMA>
                <_INTERN.SMMABZ>Violett</_INTERN.SMMABZ>
              </ROW>
              <STOP />
            </TABLE></PARM>
            """);

        var result = ArticleRecognitionColorLookup.Parse(xml, "RP.00010");

        Assert.Equal(ArticleRecognitionColorStatuses.Complete, result.Status);
        Assert.Equal("RP.00010", result.Article);
        Assert.NotNull(result.Color1);
        Assert.Equal("EFA01", result.Color1!.Feature);
        Assert.Equal("0D0D0D", result.Color1.Hex);
        Assert.Equal("Schwarz", result.Color1.Name);
        Assert.NotNull(result.Color2);
        Assert.Equal("EFA02", result.Color2!.Feature);
        Assert.Equal("7030A0", result.Color2.Hex);
        Assert.Equal("Violett", result.Color2.Name);
    }

    [Theory]
    [InlineData("0d0d0d", "0D0D0D")]
    [InlineData("#7030A0", "7030A0")]
    public void NormalizeHex_AcceptsSixDigitRgb(string value, string expected)
    {
        Assert.Equal(expected, ArticleRecognitionColorLookup.NormalizeHex(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("7030")]
    [InlineData("GG30A0")]
    [InlineData("7030A000")]
    public void NormalizeHex_RejectsUnsafeValues(string value)
    {
        Assert.Null(ArticleRecognitionColorLookup.NormalizeHex(value));
    }

    [Fact]
    public void Parse_IsIncompleteWhenOneColorIsMissing()
    {
        var xml = XDocument.Parse("""
            <PARM><TABLE>
              <ROW><KEY><ASSMMN>EFA01</ASSMMN></KEY><UYASMP.ASSMMA>0D0D0D</UYASMP.ASSMMA><_INTERN.SMMABZ>Schwarz</_INTERN.SMMABZ></ROW>
              <STOP />
            </TABLE></PARM>
            """);

        var result = ArticleRecognitionColorLookup.Parse(xml, "RP.00010");

        Assert.Equal(ArticleRecognitionColorStatuses.Incomplete, result.Status);
        Assert.NotNull(result.Color1);
        Assert.Null(result.Color2);
        Assert.Contains("EFA02", result.Message);
    }
}
