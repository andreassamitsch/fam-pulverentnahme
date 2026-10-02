using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Fam.Pulverentnahme.Web;

public sealed record MachineTankOption(string Warehouse, string WarehouseText);

/// <summary>
/// Machine/tank warehouses are read dynamically from Oxaion ULGSTP for the active company.
/// The confirmed business definition is LGLGART = '02'. There is no static EOS1/EOS2 whitelist.
///
/// The stock read reuses the confirmed LB30230R "Chargen pro Lagerort" list without an expected
/// article. The one non-zero tank row itself is the source of Article + ArticleText.
/// No product number is supplied by the browser for this decision.
/// </summary>
public sealed class MachineTankService
{
    private const int MaxListPages = 100;
    internal const string MachineTankWarehouseSql = """
        SELECT
            LG.LGLAGO,
            LG.LGBEZC
        FROM OXAION.ULGSTP AS LG
        WHERE LG.LGFIRM = @firm
          AND LG.LGLGART = N'02'
        ORDER BY LG.LGLAGO;
        """;

    private readonly OxaionClient _oxaion;
    private readonly OxaionOptions _oxaionOptions;
    private readonly OxaionSqlOptions _oxaionSql;

    public MachineTankService(
        OxaionClient oxaion,
        IOptions<OxaionOptions> oxaionOptions,
        IOptions<OxaionSqlOptions> oxaionSql)
    {
        _oxaion = oxaion;
        _oxaionOptions = oxaionOptions.Value;
        _oxaionSql = oxaionSql.Value;
    }

    public async Task<bool> IsAllowedAsync(string warehouse, CancellationToken ct)
    {
        warehouse = (warehouse ?? "").Trim();
        if (warehouse.Length == 0) return false;
        var options = await ReadOptionsAsync(ct);
        return options.Any(x => string.Equals(x.Warehouse, warehouse, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<MachineTankOption>> ReadOptionsAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_oxaionSql.ConnectionString))
            throw new InvalidOperationException("Oxaion SQL-Verbindung ist nicht konfiguriert.");

        var result = new List<MachineTankOption>();
        await using var connection = new SqlConnection(_oxaionSql.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = MachineTankWarehouseSql;
        command.CommandTimeout = 15;
        command.Parameters.AddWithValue("@firm", _oxaionOptions.Firm);

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var warehouse = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim();
            if (warehouse.Length == 0) continue;
            var text = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();
            if (result.Any(x => string.Equals(x.Warehouse, warehouse, StringComparison.OrdinalIgnoreCase)))
                continue;
            result.Add(new MachineTankOption(warehouse, string.IsNullOrWhiteSpace(text) ? warehouse : text));
        }

        return result;
    }

    public async Task<MachineStockResult> ReadStockAsync(string warehouse, CancellationToken ct)
    {
        warehouse = (warehouse ?? "").Trim();
        if (string.IsNullOrWhiteSpace(warehouse)) throw new ArgumentException("Machine warehouse is required.");

        var configured = (await ReadOptionsAsync(ct))
            .SingleOrDefault(x => string.Equals(x.Warehouse, warehouse, StringComparison.OrdinalIgnoreCase));
        if (configured is null)
            throw new ArgumentException($"Lagerort {warehouse} ist laut Oxaion ULGSTP (LGLGART=02) kein Maschinentank.");

        await using var session = await _oxaion.ConnectAsync(ct);
        var warehouseText = configured.WarehouseText;
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

        ArticleRecognitionColorsResult recognitionColors;
        try
        {
            // The confirmed characteristic lookup has its own Oxaion screen/session context.
            // Do not reuse the LB30230R tank-list context here: that can leave US17000/US21000
            // without the expected parent state and resulted in an empty recognition swatch.
            await using var colorSession = await _oxaion.ConnectAsync(ct);
            recognitionColors = await ArticleRecognitionColorLookup.ReadAsync(colorSession, current.Article, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // EFA01/EFA02 are a visual recognition aid. A lookup problem must be visible to the
            // operator but must not convert an otherwise valid machine stock into a booking result.
            recognitionColors = new ArticleRecognitionColorsResult(
                ArticleRecognitionColorStatuses.Unavailable,
                current.Article,
                null,
                null,
                $"Erkennungsfarben EFA01/EFA02 konnten nicht aus oxaion gelesen werden: {ex.Message}");
        }

        return new MachineStockResult(
            MachineStockStatuses.Unique,
            warehouse,
            current.Article,
            $"Eindeutiger Oxaion-Maschinenbestand: {current.Article} ({current.ArticleText}), Charge {current.Batch}, {current.QuantityKg:0.###} kg.",
            DateTimeOffset.UtcNow,
            nonZero,
            recognitionColors);
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
