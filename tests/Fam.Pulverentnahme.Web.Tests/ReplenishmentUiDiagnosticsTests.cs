using Xunit;

namespace Fam.Pulverentnahme.Web.Tests;

public sealed class ReplenishmentUiDiagnosticsTests
{
    [Fact]
    public void IndexLoadsDiagnosticsAndReplenishmentGuard()
    {
        var webRoot = WebRoot();
        var index = File.ReadAllText(Path.Combine(webRoot, "index.html"));

        Assert.Contains("/ui-diagnostics.js?v=20261006-tank-runtime-diag-1", index);
        Assert.Contains("/replenish-router-guard.js?v=20260909-replenish-guard-1", index);
        Assert.True(index.IndexOf("/process-mode.js", StringComparison.Ordinal) <
                    index.IndexOf("/replenish-router-guard.js", StringComparison.Ordinal));
    }

    [Fact]
    public void ReplenishmentGuardMaintainsLegacyVisibilityAndCanRestoreRouterMode()
    {
        var source = File.ReadAllText(Path.Combine(WebRoot(), "replenish-router-guard.js"));

        Assert.Contains("enforceLegacyVisibility", source);
        Assert.Contains("node.classList.remove('processModeHidden')", source);
        Assert.Contains("instruction()==='Vorgang auswählen.'", source);
        Assert.Contains("button.click()", source);
        Assert.Contains("REPLENISH_ROUTER_RECOVERY_START", source);
    }

    [Fact]
    public void DiagnosticLoggerIsServerGatedAndOmitsCredentialFields()
    {
        var source = File.ReadAllText(Path.Combine(WebRoot(), "ui-diagnostics.js"));

        Assert.Contains("diagnosticHeaderBtn", source);
        Assert.Contains("button.textContent='Diagnose'", source);
        Assert.Contains("window.FamUiConfig?.developerToolsEnabled!==true", source);
        Assert.Contains("developerTool", source);
        Assert.Contains("Diagnose kopieren", source);
        Assert.Contains("serviceWorkerControlled", source);
        Assert.Contains("activeServiceWorkerCache", source);
        Assert.Contains("serviceWorkerCacheKeys", source);
        Assert.Contains("lastMachineStockDiagnostic", source);
        Assert.Contains("stockStatusText", source);
        Assert.Contains("appScript", source);
        Assert.Contains("submitScript", source);
        Assert.Contains("workerEnhancementsScript", source);
        Assert.Contains("FAM_DIAG_VERSION_REQUEST", source);
        Assert.Contains("FIRST_CONTROLLED_START_AFTER_INSTALL", source);
        Assert.Contains("navigator.clipboard", source);
        Assert.DoesNotContain("personnelNo:", source);
        Assert.DoesNotContain("fullName:", source);
        Assert.DoesNotContain("password:", source);
    }

    [Fact]
    public void DiagnosticBlankCheckUsesAllCurrentProcessPanelsIncludingJobAbort()
    {
        var source = File.ReadAllText(Path.Combine(WebRoot(), "ui-diagnostics.js"));
        var router = File.ReadAllText(Path.Combine(WebRoot(), "process-mode.js"));

        Assert.Contains("'tank-out':'tankOutProcess'", source);
        Assert.Contains("'label-reprint':'labelReprintProcess'", source);
        Assert.Contains("'fill-new':'fillNewProcess'", source);
        Assert.Contains("'fa-consumption':'faConsumptionProcess'", source);
        Assert.Contains("'fa-abort-correction':'faAbortProcess'", source);
        Assert.Contains("'inventory':'inventoryProcess'", source);
        Assert.Contains("'faAbortProcess'", source);
        Assert.Contains("'labelReprintProcess'", source);
        Assert.Contains("if(expected)return state.visibleIds.includes(expected)", source);
        Assert.Contains("getClientRects().length", source);
        Assert.Contains("expectedPanelVisible", source);
        Assert.DoesNotContain("abortTankScan", source);
        Assert.Contains("abortOrderScan", source);
        Assert.Contains("abort.id='faAbortProcess'", router);
        Assert.Contains("faAbortProcess", router);
        Assert.Contains("OPERATION_RESULT", router);
        Assert.Contains("stage:String(b.stage||'')", router);
        Assert.Contains("/api/fa-abort-correction/resolve-source", router);
        Assert.DoesNotContain("abortTankStep", router);
        Assert.Contains("Oxaion-Fall prüfen", router);
    }

    [Fact]
    public void LabelReprintIsRecognizedAsVisibleProcessContent()
    {
        var source = File.ReadAllText(Path.Combine(WebRoot(), "ui-diagnostics.js"));

        Assert.Contains("'label-reprint':'labelReprintProcess'", source);
        Assert.Contains("'labelReprintProcess'", source);
        Assert.Contains("const expected=PROCESS_PANELS[state.activeMode]", source);
        Assert.Contains("if(expected)return state.visibleIds.includes(expected)", source);
    }

    [Fact]
    public void FirstUncontrolledPwaStartIsNormalizedBeforeProcessUse()
    {
        var source = File.ReadAllText(Path.Combine(WebRoot(), "connectivity-status.js"));

        Assert.Contains("firstStartWasUncontrolled", source);
        Assert.Contains("blockProcessStartUntilControlled", source);
        Assert.Contains("navigator.serviceWorker.ready", source);
        Assert.Contains("location.reload()", source);
        Assert.Contains("processShellProcess", source);
        Assert.Contains("scanModalOpen", source);
    }

    [Fact]
    public void ServiceWorkerV52RefreshesStaticAssetsInsteadOfReusingHttpCache()
    {
        var source = File.ReadAllText(Path.Combine(WebRoot(), "sw.js"));

        Assert.Contains("fam-pulver-v52-charge-origin-pb-20261008", source);
        Assert.Contains("new Request(url,{cache:'reload'})", source);
        Assert.Contains("/ui-diagnostics.js?v=20261006-tank-runtime-diag-1", source);
        Assert.Contains("FAM_DIAG_VERSION_REQUEST", source);
        Assert.Contains("FAM_DIAG_VERSION", source);
        Assert.Contains("/replenish-router-guard.js?v=20260909-replenish-guard-1", source);
    }

    [Fact]
    public void TankDiagnosticsPreserveLastApiResponseAfterUiStateIsCleared()
    {
        var app = File.ReadAllText(Path.Combine(WebRoot(), "app.js"));

        Assert.Contains("window.FamLastMachineStockDiagnostic=details", app);
        Assert.Contains("window.FamDiag?.log?.('MACHINE_STOCK_RESPONSE',details)", app);
        Assert.Contains("const resolvedStock=machineStock", app);
        Assert.Contains("resolvedStock?.status==='EMPTY'?'⛔ Tank ist leer.'", app);
        Assert.Contains("return resolvedStock", app);
    }

    private static string WebRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
                return Path.Combine(directory.FullName, "src", "Fam.Pulverentnahme.Web", "wwwroot");

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root containing AGENTS.md was not found.");
    }
}
