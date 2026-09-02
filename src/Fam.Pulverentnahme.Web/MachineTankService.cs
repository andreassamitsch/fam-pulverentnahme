using Microsoft.Extensions.Options;

namespace Fam.Pulverentnahme.Web;

public sealed record MachineTankOption(string Warehouse, string WarehouseText);

/// <summary>
/// Process-specific whitelist of machine/tank warehouses. The same list is used by the dropdown
/// now and is intended to be the validation target for the later machine QR scan.
///
/// The stock read reuses the confirmed LB30230R "Chargen pro Lagerort" list without an expected
/// article. The captured EOS1 list is article-independent and already returned rows of multiple
/// articles; therefore the one positive tank row itself is the source of Article + ArticleText.
/// No product number is supplied by the browser for this decision.
/// </summary>
public sealed class MachineTankService
{
    private const int MaxListPages = 100;
    private readonly OxaionClient _oxaion;
    private readonly OxaionOptions _oxaionOptions;
    private readonly MachineTankOptions _machineOptions;

    public MachineTankService(
        OxaionClient oxaion,
        IOptions<OxaionOptions> oxaionOptions,
        IOptions<MachineTankOptions> machineOptions)
    {
        _oxaion = oxaion;
        _oxaionOptions = oxaionOptions.Value;
        _machineOptions = machineOptions.Value;
    }

    public bool IsAllowed(string warehouse) =>
        _machineOptions.Warehouses.Any(x => string.Equals(x?.Trim(), warehouse?.Trim(), StringComparison.OrdinalIgnoreCase));

    public async Task<IReadOnlyList<MachineTankOption>> ReadOptionsAsync(CancellationToken ct)
    {
        var codes = _machineOptions.Warehouses
            .Select(x => (x ?? "").Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (codes.Count == 0) throw new InvalidOperationException("No machine tank warehouses are configured.");

        await using var session = await _oxaion.ConnectAsync(ct);
        var result = new List<MachineTankOption>();
        foreach (var code in codes)
            result.Add(new MachineTankOption(code, await ResolveWarehouseTextAsync(session, code, ct)));
        return result;
    }

    public async Task<MachineStockResult> ReadStockAsync(string warehouse, CancellationToken ct)
    {
        warehouse = (warehouse ?? "").Trim();
        if (string.IsNullOrWhiteSpace(warehouse)) throw new ArgumentException("Machine warehouse is required.");
        if (!IsAllowed(warehouse)) throw new ArgumentException($"Warehouse {warehouse} is not configured as a machine tank.");

        await using var session = await _oxaion.ConnectAsync(ct);
        var warehouseText = await ResolveWarehouseTextAsync(session, warehouse, ct);
        var launchInput = BuildLaunchContext(warehouse, warehouseText);
        var launch = await session.CallAsync("US30600J", "", launchInput, ct);
        OxaionSession.AssertNoFcod(launch);
        var ssid = Get(launch.Dta, "SSID");
        if (string.IsNullOrWhiteSpace(ssid))
            throw new InvalidOperationException("US30600J did not return an SSID for machine stock lookup.");

        var context = Merge(launchInput, launch.Dta);
        context["SSID"] = ssid;
        context["NOHWPgm"] = "LB30230R";
        var header = await session.CallAsync("LB30230R", "*GETHDR", context, ct);
        OxaionSession.AssertNoFcod(header);

        var page = await session.CallAsync("LB30230R", "*FIRSTLIST", ListContext(ssid, "reset"), ct);
        OxaionSession.AssertNoFcod(page);
        var allRows = new List<MachineStockRow>();
        var completed = false;
        for (var pageNumber = 1; pageNumber <= MaxListPages; pageNumber++)
        {
            allRows.AddRange(MachineStockService.ParseRows(page.Xml)
                .Where(r => string.Equals(r.Warehouse, warehouse, StringComparison.OrdinalIgnoreCase)));
            if (MachineStockService.HasStop(page.Xml))
            {
                completed = true;
                break;
            }
            page = await session.CallAsync("LB30230R", "*NEXTLIST", Dict(("SSID", ssid)), ct);
            OxaionSession.AssertNoFcod(page);
        }
        if (!completed)
            throw new InvalidOperationException($"LB30230R list did not return STOP within {MaxListPages} pages. Machine stock result is incomplete.");

        var nonZero = allRows.Where(r => r.QuantityKg != 0m).ToList();
        if (nonZero.Count == 0)
            return new MachineStockResult(MachineStockStatuses.Empty, warehouse, "", $"Auf {warehouse} wurde kein Bestand ungleich 0 gefunden.", DateTimeOffset.UtcNow, nonZero);
        if (nonZero.Count > 1)
            return new MachineStockResult(MachineStockStatuses.Ambiguous, warehouse, "", $"Auf {warehouse} wurden mehrere Bestände ungleich 0 ({nonZero.Count}) gefunden. Artikel und Mix-Charge sind nicht eindeutig.", DateTimeOffset.UtcNow, nonZero);

        var current = nonZero[0];
        if (current.QuantityKg < 0m)
            return new MachineStockResult(MachineStockStatuses.InvalidStock, warehouse, current.Article, $"Auf {warehouse} wurde ein negativer Bestand gefunden: {current.Article}, Charge {current.Batch}, {current.QuantityKg:0.###} {current.Unit}.", DateTimeOffset.UtcNow, nonZero);
        if (!string.Equals(current.Unit, "KGM", StringComparison.OrdinalIgnoreCase))
            return new MachineStockResult(MachineStockStatuses.InvalidStock, warehouse, current.Article, $"Der Maschinenbestand wird in der unerwarteten Mengeneinheit '{current.Unit}' geliefert. Automatische kg-Buchung ist gesperrt.", DateTimeOffset.UtcNow, nonZero);
        if (string.IsNullOrWhiteSpace(current.Article))
            return new MachineStockResult(MachineStockStatuses.InvalidStock, warehouse, "", $"Der positive Maschinenbestand auf {warehouse} enthält keinen eindeutigen Artikel.", DateTimeOffset.UtcNow, nonZero);

        return new MachineStockResult(
            MachineStockStatuses.Unique,
            warehouse,
            current.Article,
            $"Eindeutiger Oxaion-Maschinenbestand: {current.Article} ({current.ArticleText}), Charge {current.Batch}, {current.QuantityKg:0.###} kg.",
            DateTimeOffset.UtcNow,
            nonZero);
    }

    private async Task<string> ResolveWarehouseTextAsync(OxaionSession session, string warehouse, CancellationToken ct)
    {
        var plain = await session.CallAsync("US00006J", "*GETPLAIN", Dict(
            ("MFLD", "LAGO"),
            ("PGMN", "US30600J"),
            ("LAGO", warehouse),
            ("PFIELD", "TX_LAGO"),
            ("FIELD", "LAGO")), ct);
        OxaionSession.AssertNoFcod(plain);
        var text = Get(plain.Dta, "TX_LAGO");
        return string.IsNullOrWhiteSpace(text) ? warehouse : text;
    }

    private Dictionary<string, string> BuildLaunchContext(string warehouse, string warehouseText)
    {
        // Same confirmed LB30230R startup context as MachineStockService, but intentionally
        // without an expected article: the positive tank row supplies the article.
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["WRKB"] = "", ["DATV"] = "", ["LHKZ20"] = "", ["KSTB"] = "",
            ["TIDF"] = "", ["BWKZBZ"] = "",
            ["STARTUP"] = $"<DUFIRM>{_oxaionOptions.Firm}</DUFIRM><DUIDNV></DUIDNV><DULAGV>{warehouse}</DULAGV>",
            ["NANW"] = "", ["XLFTBZ"] = "", ["mode"] = "no-attribute-update", ["SNNR20"] = "",
            ["TX_FFMT"] = "Chargen pro Lagerort", ["TX_LAGR"] = "", ["LAGR20"] = "", ["KOBN"] = "",
            ["TX_LAGO"] = warehouseText, ["DATB"] = "", ["KOKO"] = "0", ["PONR"] = "", ["PONR20"] = "",
            ["ABCK"] = "", ["LHKZBZ"] = "", ["LHKZ"] = "", ["ABCK20"] = "", ["TX_KSTT"] = "",
            ["REPORT"] = "", ["KSTTV"] = "", ["ANWG"] = "LBS", ["KOAW"] = "", ["TX_BUKR"] = "",
            ["FMANWG"] = "LBS", ["BGNR"] = "", ["LHMT20"] = "", ["XLFT"] = "", ["BWKZ"] = "",
            ["B_BBL20"] = "", ["FFMT"] = "CO", ["LAGO20"] = "", ["FFMS"] = "CO", ["LAGR"] = "",
            ["KOPS"] = "0", ["LAPL20"] = "", ["PGMN"] = "LB30230R", ["LAGO"] = warehouse,
            ["KSTTB"] = "", ["TX_TIDF"] = "", ["SNNR"] = "", ["KOVU20"] = "", ["LAPL"] = "",
            ["BWKZ20"] = "", ["TX_WERK"] = "", ["I_TIDF"] = "", ["ABCKBZ"] = "", ["XLFT20"] = "",
            ["WRKV"] = "", ["LHMT"] = "", ["KSTV"] = "", ["BUKR"] = "", ["SSID"] = "",
            ["LHMTBZ"] = "", ["BLNR20"] = "", ["DATE20"] = "", ["BLNR"] = "0"
        };
    }

    private static Dictionary<string, string> ListContext(string ssid, string mode) => Dict(
        ("FLD", ""), ("SSID", ssid), ("PFLD", ""), ("MC-Modus", ""), ("mode", mode));

    private static string Get(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) ? value : "";

    private static Dictionary<string, string> Dict(params (string Key, string Value)[] values) =>
        values.ToDictionary(x => x.Key, x => x.Value ?? "", StringComparer.Ordinal);

    private static Dictionary<string, string> Merge(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
    {
        var result = new Dictionary<string, string>(left, StringComparer.Ordinal);
        foreach (var item in right) result[item.Key] = item.Value ?? "";
        return result;
    }
}
