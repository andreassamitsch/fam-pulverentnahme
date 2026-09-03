using System.Xml.Linq;
using Fam.Pulverentnahme.Web;
using Xunit;

namespace Fam.Pulverentnahme.Web.Tests;

public sealed class PersonnelLookupTests
{
    [Theory]
    [InlineData("446", "446")]
    [InlineData("0000000446", "446")]
    [InlineData("00123", "123")]
    public void NormalizesPersonnelNumberWithoutLeadingZeroes(string input, string expected)
    {
        Assert.Equal(expected, PersonnelService.NormalizeInput(input));
    }

    [Fact]
    public void RejectsNonNumericPersonnelInput()
    {
        Assert.Throws<ArgumentException>(() => PersonnelService.NormalizeInput("44A"));
    }

    [Fact]
    public void ParsesSearchSeedReturnedByUs14090()
    {
        var xml = XDocument.Parse("""
            <PARM><TABLE><TREE>
              <KEY><PEPENU>0000000446</PEPENU></KEY>
              <RECORD><PTXWFA>UR</PTXWFA><STTXOA>PENU</STTXOA><TEPENU>0000000446</TEPENU><PEOBID>123</PEOBID></RECORD>
            </TREE></TABLE></PARM>
            """);

        var seed = Assert.Single(PersonnelService.ParseSearchSeeds(xml));
        Assert.Equal("0000000446", seed.Fields["PEPENU"]);
        Assert.Equal("UPERS", seed.Fields["KEYTYPE"]);
        Assert.Equal("UR", seed.Fields["PTXWFA"]);
    }

    [Fact]
    public void ParsesPersonnelNumberAndFullNameFromPepenaWithoutShortCode()
    {
        var xml = XDocument.Parse("""
            <PARM><DTA>
              <PEPENU>0000000446</PEPENU>
              <PEPENA>Andreas Samitsch</PEPENA>
            </DTA></PARM>
            """);

        var person = PersonnelService.ParsePersonnel(xml);
        Assert.NotNull(person);
        Assert.Equal("446", person!.PersonnelNo);
        Assert.Equal("Andreas Samitsch", person.FullName);
    }

    [Fact]
    public void IgnoresPesakzAndPenlaeWhenPepenaIsPresent()
    {
        var xml = XDocument.Parse("""
            <PARM><DTA>
              <PEPENU>0000000446</PEPENU>
              <PESAKZ>ANSA</PESAKZ>
              <PENLAE>Falsches Namensfeld</PENLAE>
              <PEPENA>Andreas Samitsch</PEPENA>
            </DTA></PARM>
            """);

        var person = PersonnelService.ParsePersonnel(xml);
        Assert.NotNull(person);
        Assert.Equal("Andreas Samitsch", person!.FullName);
    }

    [Theory]
    [InlineData("0000000450", "45", true)]
    [InlineData("0000000451", "45", true)]
    [InlineData("0000000452", "45", true)]
    [InlineData("0000000453", "45", true)]
    [InlineData("0000000245", "45", false)]
    [InlineData("0000000345", "45", false)]
    [InlineData("0000001450", "450", false)]
    public void PersonnelPrefixMatchesOnlyAtStartOfNormalizedPepenu(string oxaionPersonnelNo, string prefix, bool expected)
    {
        Assert.Equal(expected, PersonnelService.MatchesPersonnelPrefix(oxaionPersonnelNo, prefix));
    }

    [Theory]
    [InlineData("450", "0000000450")]
    [InlineData("45", "0000000045")]
    [InlineData("0000000450", "0000000450")]
    public void BuildsTenDigitExactOxaionPersonnelNumber(string input, string expected)
    {
        Assert.Equal(expected, PersonnelService.ToOxaionPersonnelNumber(input));
    }

    [Fact]
    public void ExactSelectionUsesGetSltAtrStateBeforeChkSltv()
    {
        var getSltv = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["IWERK"] = "OLD",
            ["UNCHANGED"] = "A"
        };
        var getSltAtr = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["IWERK"] = "NEW",
            ["ATTRIBUTE_ONLY"] = "B"
        };

        var fields = PersonnelService.BuildExactSelectionFields(
            getSltv,
            getSltAtr,
            "PERSONNEL-SSID",
            "450");

        Assert.Equal("NEW", fields["IWERK"]);
        Assert.Equal("A", fields["UNCHANGED"]);
        Assert.Equal("B", fields["ATTRIBUTE_ONLY"]);
        Assert.Equal("PERSONNEL-SSID", fields["SSID"]);
        Assert.Equal("0000000450", fields["IPENU"]);
    }
}
