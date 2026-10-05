using Fam.Pulverentnahme.Web;
using Xunit;

namespace Fam.Pulverentnahme.Web.Tests;

public sealed class OperatorUiCorrectionsTests
{
    [Fact]
    public void OxaionDocumentTextsRespectConfirmedFieldLimits()
    {
        var texts = OxaionDocumentTextBuilder.Build(
            "b24a6618d0971234567890",
            "446",
            "Tankwiegung Schwund mit sehr langer Zusatzinformation");

        Assert.True(texts.DocumentText.Length <= OxaionDocumentTextBuilder.DocumentTextMaxLength);
        Assert.True(texts.MatchCode.Length <= OxaionDocumentTextBuilder.MatchCodeMaxLength);
        Assert.StartsWith("Tankwiegung Schwund", texts.DocumentText);
        Assert.StartsWith("b24a6618d097|PN446|", texts.MatchCode);
        Assert.DoesNotContain("\r", texts.DocumentText);
        Assert.DoesNotContain("\n", texts.DocumentText);
    }

    [Fact]
    public void OperatorProcessMenuUsesRequestedOrderAndNoNumberedStepHeadings()
    {
        var source = ReadWebFile("process-mode.js");

        string[] orderedModes =
        [
            "data-mode=\"replenish\"",
            "data-mode=\"fa-consumption\"",
            "data-mode=\"tank-out\"",
            "data-mode=\"fill-new\"",
            "data-mode=\"fa-abort-correction\"",
            "data-mode=\"inventory\"",
            "data-mode=\"label-reprint\""
        ];

        var previous = -1;
        foreach (var marker in orderedModes)
        {
            var index = source.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(index > previous, $"Expected {marker} after the previous process.");
            previous = index;
        }

        Assert.DoesNotContain("<h2>2 ·", source);
        Assert.DoesNotContain("<h2>3 ·", source);
        Assert.DoesNotContain("<h2>4 ·", source);
        Assert.DoesNotContain("<h2>5 ·", source);
        Assert.Contains("outWeightCommitted", source);
        Assert.Contains("fam-process-complete", source);
    }

    [Fact]
    public void JobAbortStartsWithFaScanAndResolvesTankFromOxaion()
    {
        var source = ReadWebFile("process-mode.js");

        Assert.Contains("Fertigungsauftrag des abgebrochenen Jobs scannen.", source);
        Assert.Contains("/api/fa-abort-correction/resolve-source", source);
        Assert.Contains("Automatisch ermittelter Tank", source);
        Assert.DoesNotContain("abortTankScan", source);
        Assert.DoesNotContain("abortTankStep", source);
    }

    [Fact]
    public void MissingRecognitionColorsDoNotRenderAnEmptyColorBox()
    {
        var source = ReadWebFile("process-mode.js");

        Assert.Contains("if(!valid(a)&&!valid(b))return ''", source);
    }

    [Fact]
    public void InventoryRowsShowOneFlatLocationBatchQuantityLine()
    {
        var source = ReadWebFile("process-mode.js");

        Assert.Contains("inventoryLocation", source);
        Assert.Contains("r.storageBin?`${html(r.warehouse)} / ${html(r.storageBin)}`:html(r.warehouse)", source);
        Assert.DoesNotContain("inventoryWarehouse", source);
        Assert.DoesNotContain("warehouses:new Map()", source);
    }

    [Fact]
    public void InventoryOverviewContainsTankSectionRecognitionColorsAndDedicatedEndpoint()
    {
        var source = ReadWebFile("process-mode.js");
        Assert.Contains("Maschinentanks", source);
        Assert.Contains("Pulverlager", source);
        Assert.Contains("/api/inventory/overview", source);
        Assert.Contains("recognitionColors", source);
        Assert.Contains("renderInventoryTanks", source);
    }

    [Fact]
    public void DeveloperControlsAreHiddenUntilServerEnablesThem()
    {
        var index = ReadWebFile("index.html");
        var css = ReadWebFile("worker-ui.css");
        var uiConfig = ReadWebFile("ui-config.js");

        Assert.Contains("developerTool", index);
        Assert.Contains("/api/ui-config", uiConfig);
        Assert.Contains("developerToolsEnabled:false", uiConfig);
        Assert.Contains("environment:'STAGING'", uiConfig);
        Assert.Contains("environmentBadge", index);
        Assert.Contains("html:not(.developer-tools-enabled) .developerTool", css);
    }

    [Fact]
    public void TankOutOperatorTextDoesNotExposeCorrectionBookingKeys()
    {
        var source = ReadWebFile("process-mode.js");

        Assert.DoesNotContain("I2 Schwund", source);
        Assert.DoesNotContain("I1 Mehrbestand", source);
        Assert.DoesNotContain("Vor LF/LE", source);
        Assert.Contains("Der Bestand wird vor der Auslagerung", source);
    }

    [Fact]
    public void InventoryTankRecognitionSwatchKeepsFlexLayout()
    {
        var source = ReadWebFile("process-mode.js");

        Assert.Contains(".inventoryTank .processSwatch{display:inline-flex", source);
        Assert.Contains(".inventoryTank>div>b,.inventoryTank>div>span{display:block}", source);
        Assert.Contains("linear-gradient(90deg", source);
        Assert.Contains("style=\"background:${background}\"", source);
        Assert.DoesNotContain("<i style=\"${bg(a)}\"></i>", source);
        Assert.DoesNotContain(".inventoryTank b,.inventoryTank span{display:block}", source);
    }

    private static string ReadWebFile(string name)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
                return File.ReadAllText(Path.Combine(directory.FullName, "src", "Fam.Pulverentnahme.Web", "wwwroot", name));

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root containing AGENTS.md was not found.");
    }
}
