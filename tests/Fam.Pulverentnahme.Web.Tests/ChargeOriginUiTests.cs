using Xunit;

namespace Fam.Pulverentnahme.Web.Tests;

public sealed class ChargeOriginUiTests
{
    [Fact]
    public void DedicatedMenuHasManualArticleBatchAndExistingQrScanner()
    {
        var mode = WebFile("process-mode.js");
        var origin = WebFile("charge-origin-ui.js");

        Assert.Contains("data-mode=\"charge-origin\"", mode);
        Assert.Contains("id='chargeOriginProcess'", mode);
        Assert.Contains("id=\"chargeOriginArticle\"", mode);
        Assert.Contains("id=\"chargeOriginBatch\"", mode);
        Assert.Contains("id=\"chargeOriginScan\"", mode);
        Assert.Contains("id=\"chargeOriginSearch\"", mode);
        Assert.Contains("scanQrCode({title:'Chargenetikett scannen'", origin);
        Assert.Contains("parseChargeQr(raw)", origin);
        Assert.Contains("Artikel+++Charge", origin);
        Assert.Contains("/api/charge-origin?article=", origin);
        Assert.Contains("encodeURIComponent(query.article)", origin);
        Assert.Contains("encodeURIComponent(query.batch)", origin);
    }

    [Fact]
    public void InventoryTankAndStockRowsOpenOriginWithoutAdditionalManualInput()
    {
        var mode = WebFile("process-mode.js");

        Assert.Contains("data-inventory-tank-index", mode);
        Assert.Contains("showInventoryTankDetails", mode);
        Assert.Contains("Maschinentankdetails", mode);
        Assert.Contains("id=\"inventoryTankDetailOrigin\"", mode);
        Assert.Contains("id=\"inventoryDetailOrigin\"", mode);
        Assert.Contains("window.FamChargeOriginUi?.show?.(row.article,row.batch", mode);
        Assert.Contains("showInventoryPositionDetails(index)", mode);
        Assert.Contains("showInventoryTankDetails(index)", mode);
    }

    [Fact]
    public void OriginIsReadOnlyOnlineAndDisplaysBaseBatchesWithMetadata()
    {
        var origin = WebFile("charge-origin-ui.js");

        Assert.Contains("Array.isArray(data.baseBatches)", origin);
        Assert.Contains("item?.batch", origin);
        Assert.DoesNotContain("['productionOrder','Fertigungsauftrag']", origin);
        Assert.Contains("function ensureStyle()", origin);
        Assert.Contains("dataset.originBound", origin);
        Assert.Contains("function closeOriginModal(returnToDetails=false)", origin);
        Assert.Contains("id=\"chargeOriginClose\"", origin);
        Assert.Contains("originCloseButton", origin);
        Assert.Contains("window.FamChargeOriginUi?.bind?.()", WebFile("process-mode.js"));
        Assert.Contains("'Lieferant'", origin);
        Assert.Contains("'Bestellung'", origin);
        Assert.Contains("'Wareneingang'", origin);
        Assert.Contains("requestVersion", origin);
        Assert.Contains("Keine Grundchargen ermittelt", origin);
        Assert.Contains("^(RP|PB)", origin); // PB prefix is explicitly supported.
        Assert.DoesNotContain("localStorage", origin);
        Assert.DoesNotContain("indexedDB", origin);
        Assert.DoesNotContain("method:'POST'", origin);
    }

    [Fact]
    public void ProcessDiagnosticsRecognizeOriginAndPwaCachesCorrectAsset()
    {
        var diag = WebFile("ui-diagnostics.js");
        var shell = WebFile("process-shell.js");
        var html = WebFile("index.html");
        var sw = WebFile("sw.js");

        Assert.Contains("'charge-origin':'chargeOriginProcess'", diag);
        Assert.Contains("'chargeOriginProcess'", diag);
        Assert.Contains("window.FamChargeOriginUi?.reset?.()", shell);
        Assert.Contains("fam-pulver-v53-charge-origin-ui-fix-20261008", sw);
        foreach (var url in new[]
        {
            "/charge-origin-ui.js?v=20261008-origin-ui-fix-2",
            "/process-mode.js?v=20261008-origin-ui-fix-2",
            "/ui-diagnostics.js?v=20261008-charge-origin-1"
        })
        {
            Assert.Contains(url, html);
            Assert.Contains(url, sw);
        }
    }

    private static string WebFile(string name)
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
