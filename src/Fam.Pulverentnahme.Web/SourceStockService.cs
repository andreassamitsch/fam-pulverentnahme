using System.Globalization;
using System.Xml.Linq;
using Microsoft.Extensions.Options;

namespace Fam.Pulverentnahme.Web;

public sealed record SourceWarehouseOption(
    string Warehouse,
    string WarehouseText,
    decimal QuantityKg,
    int BatchCount);

public sealed record SourceStockPosition(
    string Warehouse,
    string WarehouseText,
    string StorageBin,
    string Batch,
    decimal QuantityKg,
    string Unit);

public sealed record SourceStockValidationResult(
    bool IsValid,
    string Message,
    IReadOnlyList<SourceStockPosition> CurrentPositions);

/// <summary>
/// Read-only lookup for selectable replenishment source stock.
///
/// Confirmed by the 2026-09-02 JET captures:
/// - US30600J -> LB30340R (FFMT/FFMS=CL): "Chargen und Lagerorte pro Artikel".
///   The list contains article + warehouse + batch + warehouse stock.
/// - US30600J -> LB30430R (FFMT/FFMS=PT): "Lagerplätze pro Artikel und -ort".
///   The list contains the exact internal storage-bin key + batch + stock.
/// - US00006J *GETPLAIN resolves the warehouse description for US30600J/LAGO.
/// - LAG1626 means the selected warehouse has no storage-bin organization. In that exact case
///   the already proven LB30230R "Chargen pro Lagerort" flow is used and StorageBin stays empty.
///
/// No saved/user-specific Oxaion filter is used. Complete lists are read to STOP and positive
/// stock is evaluated by the backend. Only exact Oxaion-returned keys are offered to the client
/// and accepted during the pre-write validation.
/// </summary>
public sealed class SourceStockService
{
    private const int MaxListPages = 100;
    private readonly OxaionClient _oxaion;
    private readonly OxaionOptions _options;
    private readonly MachineStockService _machineStock;

    public SourceStockService(
        OxaionClient oxaion,
        IOptions<OxaionOptions> options,
        MachineStockService machineStock)
    {
        _oxaion = oxaion;
        _options = options.Value;
        _machineStock = machineStock;
    }

    public async Task<IReadOnlyList<SourceWarehouseOption>> ReadWarehousesAsync(string article, CancellationToken ct)
    {
        article = (article ?? "").Trim();
        if (string.IsNullOrWhiteSpace(article)) throw new ArgumentException("Article is required.");

        await using var session = await _oxaion.ConnectAsync(ct);
        var rows = await ReadArticleWarehouseRowsAsync(session, article, ct);
        var positive = rows.Where(r => r.QuantityKg > 0m).ToList();
        var result = new List<SourceWarehouseOption>();
        foreach (var group in positive
                     .GroupBy(r => r.Warehouse, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var text = await ResolveWarehouseTextAsync(session, group.Key, ct);
            result.Add(new SourceWarehouseOption(
                group.Key,
                text,
                group.Sum(x => x.QuantityKg),
                group.Select(x => x.Batch).Distinct(StringComparer.Ordinal).Count()));
        }
        return result;
    }

    public async Task<IReadOnlyList<SourceStockPosition>> ReadPositionsAsync(
        string article,
        string warehouse,
        CancellationToken ct)
    {
        article = (article ?? "").Trim();
        warehouse = (warehouse ?? "").Trim();
        if (string.IsNullOrWhiteSpace(article) || string.IsNullOrWhiteSpace(warehouse))
            throw new ArgumentException("Article and warehouse are required.");

        await using var session = await _oxaion.ConnectAsync(ct);
        return await ReadPositionsAsync(session, article, warehouse, ct);
    }

    public async Task<SourceStockValidationResult> ValidateSourcesAsync(
        string article,
        IReadOnlyList<AdditionalPowderSource> sources,
        CancellationToken ct)
    {
        article = (article ?? "").Trim();
        if (string.IsNullOrWhiteSpace(article)) throw new ArgumentException("Article is required.");
        if (sources is null || sources.Count == 0) throw new ArgumentException("At least one replenishment source is required.");

        var duplicate = sources
            .GroupBy(s => SourceKey(s.Warehouse, s.StorageBin, s.Batch), StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            return new SourceStockValidationResult(
                false,
                "Dieselbe Oxaion-Bestandsposition wurde mehrfach als Nachfüllquelle ausgewählt. Bitte die Menge in einer Nachfüllcharge zusammenfassen.",
                []);
        }

        await using var session = await _oxaion.ConnectAsync(ct);
        var current = new List<SourceStockPosition>();
        foreach (var warehouseGroup in sources.GroupBy(s => s.Warehouse, StringComparer.OrdinalIgnoreCase))
        {
            var positions = await ReadPositionsAsync(session, article, warehouseGroup.Key, ct);
            current.AddRange(positions);

            foreach (var source in warehouseGroup)
            {
                var matches = positions.Where(p =>
                    string.Equals(p.Warehouse, source.Warehouse, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(p.StorageBin ?? "", source.StorageBin ?? "", StringComparison.Ordinal)
                    && string.Equals(p.Batch, source.Batch, StringComparison.Ordinal)).ToList();

                if (matches.Count != 1)
                {
                    return new SourceStockValidationResult(
                        false,
                        $"Nachfüllcharge {source.Batch}: Die ausgewählte Oxaion-Bestandsposition {DisplayLocation(source.Warehouse, source.StorageBin)} ist nicht mehr eindeutig mit positivem Bestand vorhanden.",
                        current);
                }

                var available = matches[0].QuantityKg;
                if (source.AmountKg <= 0m || available + 0.0005m < source.AmountKg)
                {
                    return new SourceStockValidationResult(
                        false,
                        $"Nachfüllcharge {source.Batch}: Angefordert {source.AmountKg:0.###} kg, aktuell verfügbar {available:0.###} kg auf {DisplayLocation(source.Warehouse, source.StorageBin)}.",
                        current);
                }
            }
        }

        return new SourceStockValidationResult(
            true,
            "Alle Nachfüllquellen wurden unmittelbar vor der Buchung in oxaion bestätigt.",
            current);
    }

    public static IReadOnlyList<(string Warehouse, string Article, string Batch, decimal QuantityKg)> ParseArticleWarehouseRows(XDocument xml)
    {
        var rows = new List<(string Warehouse, string Article, string Batch, decimal QuantityKg)>();
        foreach (var row in xml.Descendants("ROW"))
        {
            var key = row.Element("KEY");
            if (key is null) continue;
            var warehouse = key.Element("LALAGO")?.Value.Trim() ?? "";
            var article = key.Element("LAIDNR")?.Value.Trim() ?? "";
            var batch = key.Element("LAPONR")?.Value.Trim() ?? "";
            var quantityText = row.Element("LLAWEL01PONR.LALABE")?.Value.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(warehouse) || string.IsNullOrWhiteSpace(article) || string.IsNullOrWhiteSpace(batch)) continue;
            rows.Add((warehouse, article, batch, ParseNumber(quantityText)));
        }
        return rows;
    }

    public static IReadOnlyList<SourceStockPosition> ParsePositionRows(XDocument xml, string warehouseText)
    {
        var rows = new List<SourceStockPosition>();
        foreach (var row in xml.Descendants("ROW"))
        {
            var key = row.Element("KEY");
            if (key is null) continue;
            var warehouse = key.Element("LPLAGO")?.Value.Trim() ?? "";
            var article = key.Element("LPIDNR")?.Value.Trim() ?? "";
            var storageBin = key.Element("LPLAPL")?.Value.Trim()
                             ?? row.Element("LLPWEP.LPLAPL")?.Value.Trim()
                             ?? "";
            var batch = key.Element("LPPONR")?.Value.Trim()
                        ?? row.Element("LLPWEP.LPPONR")?.Value.Trim()
                        ?? "";
            var quantityText = row.Element("LLPWEP.LPLABE")?.Value.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(warehouse) || string.IsNullOrWhiteSpace(article) || string.IsNullOrWhiteSpace(batch)) continue;
            var (quantity, unit) = MachineStockService.ParseQuantity(quantityText);
            if (string.Equals(unit, "kg", StringComparison.OrdinalIgnoreCase)) unit = "KGM";
            rows.Add(new SourceStockPosition(warehouse, warehouseText, storageBin, batch, quantity, unit));
        }
        return rows;
    }

    private async Task<IReadOnlyList<(string Warehouse, string Article, string Batch, decimal QuantityKg)>> ReadArticleWarehouseRowsAsync(
        OxaionSession session,
        string article,
        CancellationToken ct)
    {
        var context = BuildInquiryContext(article, "", "", "CL", "Chargen und Lagerorte pro Artikel", "LB30340R");
        var ssid = await LaunchAsync(session, context, "LB30340R", "LB30340", ct);
        var pages = await ReadAllPagesAsync(session, "LB30340R", ssid, ct);
        return pages.SelectMany(ParseArticleWarehouseRows)
            .Where(r => string.Equals(r.Article, article, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private async Task<IReadOnlyList<SourceStockPosition>> ReadPositionsAsync(
        OxaionSession session,
        string article,
        string warehouse,
        CancellationToken ct)
    {
        var canonicalText = await ResolveWarehouseTextAsync(session, warehouse, ct);
        try
        {
            var context = BuildInquiryContext(article, warehouse, canonicalText, "PT", "Lagerplätze pro Artikel und -ort", "LB30430R");
            var ssid = await LaunchAsync(session, context, "LB30430R", "LB30430", ct);
            var pages = await ReadAllPagesAsync(session, "LB30430R", ssid, ct);
            var rows = pages.SelectMany(x => ParsePositionRows(x, canonicalText))
                .Where(p => string.Equals(p.Warehouse, warehouse, StringComparison.OrdinalIgnoreCase))
                .Where(p => p.QuantityKg > 0m)
                .ToList();

            if (rows.Any(p => !string.Equals(p.Unit, "KGM", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"LB30430R returned an unexpected unit for {article}/{warehouse}; only kg/KGM is accepted for replenishment.");

            return rows;
        }
        catch (OxaionRejectedException ex) when (string.Equals(ex.Code, "LAG1626", StringComparison.OrdinalIgnoreCase))
        {
            var stock = await _machineStock.ReadAsync(session, warehouse, article, canonicalText, null, ct);
            var rows = stock.Rows
                .Where(r => string.Equals(r.Article, article, StringComparison.OrdinalIgnoreCase))
                .Where(r => r.QuantityKg > 0m)
                .Select(r => new SourceStockPosition(
                    r.Warehouse,
                    canonicalText,
                    "",
                    r.Batch,
                    r.QuantityKg,
                    r.Unit))
                .ToList();

            if (rows.Any(p => !string.Equals(p.Unit, "KGM", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"LB30230R returned an unexpected unit for no-bin warehouse {article}/{warehouse}; only KGM is accepted for replenishment.");

            return rows;
        }
    }

    private async Task<string> LaunchAsync(
        OxaionSession session,
        Dictionary<string, string> launchInput,
        string program,
        string noHwProgram,
        CancellationToken ct)
    {
        var launch = await session.CallAsync("US30600J", "", launchInput, ct);
        OxaionSession.AssertNoFcod(launch);
        var ssid = Get(launch.Dta, "SSID");
        if (string.IsNullOrWhiteSpace(ssid))
            throw new InvalidOperationException($"US30600J did not return an SSID for {program}.");

        var headerInput = Merge(launchInput, launch.Dta);
        headerInput["SSID"] = ssid;
        headerInput["NOHWPgm"] = noHwProgram;
        var header = await session.CallAsync(program, "*GETHDR", headerInput, ct);
        OxaionSession.AssertNoFcod(header);
        return ssid;
    }

    private static async Task<IReadOnlyList<XDocument>> ReadAllPagesAsync(
        OxaionSession session,
        string program,
        string ssid,
        CancellationToken ct)
    {
        var pages = new List<XDocument>();
        var page = await session.CallAsync(program, "*FIRSTLIST", ListContext(ssid, "reset"), ct);
        OxaionSession.AssertNoFcod(page);
        for (var pageNumber = 1; pageNumber <= MaxListPages; pageNumber++)
        {
            pages.Add(page.Xml);
            if (MachineStockService.HasStop(page.Xml)) return pages;
            page = await session.CallAsync(program, "*NEXTLIST", Dict(("SSID", ssid)), ct);
            OxaionSession.AssertNoFcod(page);
        }
        throw new InvalidOperationException($"{program} list did not return STOP within {MaxListPages} pages.");
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

    private Dictionary<string, string> BuildInquiryContext(
        string article,
        string warehouse,
        string warehouseText,
        string format,
        string formatText,
        string program)
    {
        var context = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["WRKB"] = "", ["DATV"] = "", ["LHKZ20"] = "", ["KSTB"] = "",
            ["TIDF"] = format == "PT" ? article : "", ["BWKZBZ"] = "", ["NANW"] = "", ["TSAKZ"] = "",
            ["XLFTBZ"] = "", ["mode"] = "no-attribute-update", ["SNNR20"] = "", ["TX_FFMT"] = formatText,
            ["TX_LAGR"] = "", ["LAGR20"] = "", ["KOBN"] = "", ["TX_LAGO"] = warehouseText,
            ["DATB"] = "", ["KOKO"] = "0", ["PONR"] = "", ["PONR20"] = "", ["ABCK"] = "",
            ["LHKZBZ"] = "", ["LHKZ"] = "", ["ABCK20"] = "", ["TX_KSTT"] = "", ["REPORT"] = "",
            ["KSTTV"] = "", ["ANWG"] = "LBS", ["KOAW"] = "", ["TX_BUKR"] = "", ["INBR"] = "",
            ["FMANWG"] = "LBS", ["BGNR"] = "", ["LHMT20"] = "", ["XLFT"] = "", ["BWKZ"] = "",
            ["B_BBL20"] = "", ["FFMT"] = format, ["LAGO20"] = "", ["FFMS"] = format, ["LAGR"] = "",
            ["KOPS"] = "0", ["LAPL20"] = "", ["PGMN"] = program, ["LAGO"] = warehouse,
            ["KSTTB"] = "", ["TX_TIDF"] = "", ["SNNR"] = "", ["KOVU20"] = "", ["BKFM"] = "",
            ["LAPL"] = "", ["KEYTYPE"] = "P", ["BWKZ20"] = "", ["TX_WERK"] = "", ["I_TIDF"] = article,
            ["ABCKBZ"] = "", ["XLFT20"] = "", ["WRKV"] = "", ["LHMT"] = "", ["KSTV"] = "",
            ["BUKR"] = "", ["SSID"] = "", ["LHMTBZ"] = "", ["BLNR20"] = "", ["DATE20"] = "",
            ["NEXTPGM"] = program, ["BLNR"] = "0"
        };
        if (format == "PT")
            context["STARTUP"] = $"<DUFIRM>{_options.Firm}</DUFIRM><DUIDNV>{article}</DUIDNV><DULAGV>{warehouse}</DULAGV>";
        return context;
    }

    private static decimal ParseNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0m;
        var normalized = value.Trim();
        if (normalized.Contains(',') && normalized.Contains('.'))
            normalized = normalized.LastIndexOf(',') > normalized.LastIndexOf('.')
                ? normalized.Replace(".", "").Replace(',', '.')
                : normalized.Replace(",", "");
        else if (normalized.Contains(',')) normalized = normalized.Replace(',', '.');
        if (!decimal.TryParse(normalized, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var quantity))
            throw new FormatException($"Oxaion quantity '{value}' could not be parsed safely.");
        return quantity;
    }

    private static string SourceKey(string warehouse, string storageBin, string batch) =>
        $"{warehouse?.Trim().ToUpperInvariant()}\u001f{storageBin?.Trim()}\u001f{batch?.Trim()}";

    private static string DisplayLocation(string warehouse, string storageBin) =>
        string.IsNullOrWhiteSpace(storageBin) ? warehouse : $"{warehouse}/{storageBin}";

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
