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
    public void ParsesCodeAndFullNameReturnedByUs14000()
    {
        var xml = XDocument.Parse("""
            <PARM><DTA>
              <PEPENU>0000000446</PEPENU>
              <PESAKZ>ANSA</PESAKZ>
              <PENLAE>Andreas Samitsch</PENLAE>
            </DTA></PARM>
            """);

        var person = PersonnelService.ParsePersonnel(xml);
        Assert.NotNull(person);
        Assert.Equal("446", person!.PersonnelNo);
        Assert.Equal("ANSA", person.Code);
        Assert.Equal("Andreas Samitsch", person.FullName);
    }
}
