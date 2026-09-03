using Fam.Pulverentnahme.Web;
using Xunit;

namespace Fam.Pulverentnahme.Web.Tests;

public sealed class OxaionClientParsingTests
{
    [Fact]
    public void ParsesValidXmlWithDiagnosticContext()
    {
        var xml = OxaionClient.ParseXml("<PARM><DTA><SSID>ABC</SSID></DTA></PARM>", "US14090J *SEARCH", 200, "text/xml");

        Assert.Equal("ABC", xml.Descendants("SSID").Single().Value);
    }

    [Fact]
    public void InvalidHtmlIdentifiesFailingOxaionCallWithoutEchoingPayload()
    {
        const string payload = "<!DOCTYPE html><html><body>Andreas Samitsch</body></html>";

        var ex = Assert.Throws<InvalidOperationException>(() =>
            OxaionClient.ParseXml(payload, "US14090J *SEARCH", 200, "text/html"));

        Assert.Contains("US14090J *SEARCH", ex.Message);
        Assert.Contains("HTTP 200", ex.Message);
        Assert.Contains("Content-Type text/html", ex.Message);
        Assert.Contains("HTML response", ex.Message);
        Assert.DoesNotContain("Andreas Samitsch", ex.Message);
    }

    [Fact]
    public void EmptyResponseIsReportedExplicitly()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            OxaionClient.ParseXml("", "US14000J *READ", 200, null));

        Assert.Contains("US14000J *READ", ex.Message);
        Assert.Contains("empty response", ex.Message);
    }
}
