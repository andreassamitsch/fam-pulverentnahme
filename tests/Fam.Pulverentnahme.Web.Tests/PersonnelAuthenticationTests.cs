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

    [Theory]
    [InlineData(5)]
    [InlineData(30)]
    [InlineData(480)]
    [InlineData(1440)]
    public void AcceptsSupportedPersonnelIdleTimeouts(int minutes)
    {
        Assert.Equal(minutes, RuntimeConfigurationService.ValidatePersonnelIdleTimeoutMinutes(minutes));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(1441)]
    public void RejectsUnsupportedPersonnelIdleTimeouts(int minutes)
    {
        Assert.Throws<ArgumentException>(() => RuntimeConfigurationService.ValidatePersonnelIdleTimeoutMinutes(minutes));
    }

    [Fact]
    public void PersonnelIdleTimeoutExpiresExactlyAtConfiguredBoundary()
    {
        var last = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

        Assert.False(PersonnelAuthenticationSession.IsIdleExpired(last, last.AddMinutes(29).AddSeconds(59), 30));
        Assert.True(PersonnelAuthenticationSession.IsIdleExpired(last, last.AddMinutes(30), 30));
    }

    [Fact]
    public void FrontendIdleTrackingIsDrivenByUserInputAndDedicatedActivityEndpoint()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "Fam.Pulverentnahme.Web", "wwwroot", "personnel-auth.js"));
        var connectivity = File.ReadAllText(Path.Combine(root, "src", "Fam.Pulverentnahme.Web", "wwwroot", "connectivity-status.js"));

        Assert.Contains("/api/personnel/activity", source);
        Assert.Contains("document.addEventListener('pointerdown',activity,true)", source);
        Assert.Contains("document.addEventListener('keydown',activity,true)", source);
        Assert.Contains("Wegen Inaktivität automatisch abgemeldet", source);
        Assert.DoesNotContain("/api/personnel/activity", connectivity);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root containing AGENTS.md was not found.");
    }
}
