using Fam.Pulverentnahme.Web;
using Xunit;

namespace Fam.Pulverentnahme.Web.Tests;

public sealed class PersonnelAuthenticationTests
{
    [Theory]
    [InlineData("1", "78")]
    [InlineData("2", "7B")]
    [InlineData("12", "78CE")]
    [InlineData("731486", "7ECF2EE5714D")]
    [InlineData("123456789987654321", "78CE2CE57C4DCC165A226EB61108343F3EB7")]
    [InlineData("abcdefggfedcba", "283F7CB52C1D3F49057E32E2455C")]
    [InlineData("aBcdefGgfedcba", "28BE7CB52C1DBC49057E32E2455C")]
    public void EncodesVerifiedSyncosVectors(string password, string expected)
    {
        Assert.Equal(expected, SyncosLegacyPasswordCodec.EncodeVerifiedAlphanumeric(password));
        Assert.True(SyncosLegacyPasswordCodec.MatchesStoredHex(password, expected));
    }

    [Fact]
    public void ComparisonRejectsDifferentPassword()
    {
        Assert.False(SyncosLegacyPasswordCodec.MatchesStoredHex("731487", "7ECF2EE5714D"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1234567899876543210")]
    [InlineData("abc-123")]
    public void RejectsInputOutsideVerifiedRange(string password)
    {
        Assert.Throws<ArgumentException>(() => SyncosLegacyPasswordCodec.EncodeVerifiedAlphanumeric(password));
    }
}
