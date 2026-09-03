using System.Globalization;
using System.Xml.Linq;
using Microsoft.Extensions.Options;

namespace Fam.Pulverentnahme.Web;

public static class MachineStockStatuses
{
    public const string Unique = "UNIQUE";
    public const string Empty = "EMPTY";
    public const string WrongArticle = "WRONG_ARTICLE";
    public const string InvalidStock = "INVALID_STOCK";
    public const string Ambiguous = "AMBIGUOUS";
}

public sealed record MachineStockRow(
    string Warehouse,
    string Article,
    string ArticleText,
    string Batch,
    string ManufacturerBatch,
    decimal QuantityKg,
    string Unit,
    string LastBookingTimestamp);

public sealed record MachineStockResult(
    string Status,
    string Warehouse,
    string Article,
    string Message,
    DateTimeOffset ReadAt,
    IReadOnlyList<MachineStockRow> Rows,
    ArticleRecognitionColorsResult? RecognitionColors = null);

public sealed class MachineStockConflictException(string message) : Exception(message);

/// <summary>
/// Read-only Oxaion lookup for the current non-zero batch stock on one machine warehouse.
/// The program/action sequence is reconstructed from the captured JET data stream of 2026-09-01.
/// The captured selection is LLAWEP.LALABE &lt;&gt; 0. The backend deliberately does not depend
/// on a saved/user-specific Oxaion filter: it reads the complete LB30230R warehouse list and
/// evaluates the confirmed non-zero condition itself. This also exposes a positive stock of a
/// different article instead of incorrectly interpreting it as an empty machine.
/// </summary>
public sealed class MachineStockService
{
    private const int MaxListPages = 100;
    private readonly OxaionClient _oxaion;
    private readonly OxaionOptions _options;

    public MachineStockService(OxaionClient oxaion, IOptions<OxaionOptions> options)
    {
        _oxaion = oxaion;
        _options = options.Value;
    }

    public async Task<MachineStockResult> ReadAsync(
        string warehouse,
        string article,
        string? warehouseText,
        string? articleText,
        CancellationToken ct)
    {
        await using var session = await _oxaion.ConnectAsync(ct);
        return await ReadAsync(session, warehouse, article, warehouseText, articleText, ct);
    }

    public async Task<MachineStockResult> ReadAsync(
        OxaionSession session,
        string warehouse,
        string article,
        string? warehouseText,
        string? articleText,
        CancellationToken ct)
    {
        warehouse = (warehouse ?? "").Trim();
        article = (article ?? "").Trim();
        if (string.IsNullOrWhiteSpace(warehouse) || string.IsNullOrWhiteSpace(article))
            throw new ArgumentException("Warehouse and article are required for machine stock lookup.");

        var launchInput = BuildLaunchContext(warehouse, article, warehouseText, articleText);

        // The captured interactive request carried the SSID of its parent screen and US30600J
        // returned a new SSID for LB30230R. A backend request has no parent JET screen, therefore
        // it sends SSID explicitly empty and requires US30600J to return a fresh SSID. This call is
        // read-only; STAGING live confirmation of this backend startup detail is still required.
        var launch = await session.CallAsync("US30600J", "", launchInput, ct);
        OxaionSession.AssertNoFcod(launch);
        var ssid = Get(launch.Dta, "SSID");
        if (string.IsNullOrWhiteSpace(ssid))
            throw new InvalidOperationException("US30600J did not return an SSID for LB30230R.");

        var context = Merge(launchInput, launch.Dta);
        context["SSID"] = ssid;
        context["NOHWPgm"] = "LB30230R";

        var header = await session.CallAsync("LB30230R", "*GETHDR", context, ct);
        OxaionSession.AssertNoFcod(header);

        // The unfiltered captured FIRSTLIST for EOS1 returned all 25 charge rows, including
        // other articles and zero stock, and ended with STOP. Do not rely on GETFILTER/LOADSET:
        // the saved filter can be renamed, deleted or unavailable to another runtime user.
        var page = await session.CallAsync("LB30230R", "*FIRSTLIST", ListContext(ssid, "reset"), ct);
        OxaionSession.AssertNoFcod(page);

        var allWarehouseRows = new List<MachineStockRow>();
        var completed = false;
        for (var pageNumber = 1; pageNumber <= MaxListPages; pageNumber++)
        {
            allWarehouseRows.AddRange(ParseRows(page.Xml)
                .Where(r => string.Equals(r.Warehouse, warehouse, StringComparison.OrdinalIgnoreCase)));

            if (HasStop(page.Xml))
            {
                completed = true;
                break;
            }

            page = await session.CallAsync("LB30230R", "*NEXTLIST", Dict(("SSID", ssid)), ct);
            OxaionSession.AssertNoFcod(page);
        }

        if (!completed)
            throw new InvalidOperationException($"LB30230R list did not return STOP within {MaxListPages} pages. Machine stock result is incomplete.");

        // Exact condition captured from the Oxaion selection dialog:
        // field LLAWEP.LALABE, operator '<>', comparison value 0.
        var nonZeroRows = allWarehouseRows.Where(r => r.QuantityKg != 0m).ToList();

        if (nonZeroRows.Count == 0)
        {
            return new MachineStockResult(
                MachineStockStatuses.Empty,
                warehouse,
                article,
                $"Auf {warehouse} wurde kein Bestand ungleich 0 gefunden. Die Maschine ist laut aktueller Oxaion-Liste leer.",
                DateTimeOffset.UtcNow,
                nonZeroRows);
        }

        if (nonZeroRows.Count > 1)
        {
            return new MachineStockResult(
                MachineStockStatuses.Ambiguous,
                warehouse,
                article,
                $"Auf {warehouse} wurden mehrere Bestände ungleich 0 ({nonZeroRows.Count}) gefunden. Keine automatische Auswahl zulässig.",
                DateTimeOffset.UtcNow,
                nonZeroRows);
        }

        var current = nonZeroRows[0];
        if (current.QuantityKg < 0m)
        {
            return new MachineStockResult(
                MachineStockStatuses.InvalidStock,
                warehouse,
                article,
                $"Auf {warehouse} wurde ein negativer Bestand gefunden: {current.Article}, Charge {current.Batch}, {current.QuantityKg:0.###} {current.Unit}. Vorgang muss geklärt werden.",
                DateTimeOffset.UtcNow,
                nonZeroRows);
        }

        if (!string.Equals(current.Unit, "KGM", StringComparison.OrdinalIgnoreCase))
        {
            return new MachineStockResult(
                MachineStockStatuses.InvalidStock,
                warehouse,
                article,
                $"Der Maschinenbestand wird in der unerwarteten Mengeneinheit '{current.Unit}' geliefert. Automatische kg-Buchung ist gesperrt.",
                DateTimeOffset.UtcNow,
                nonZeroRows);
        }

        if (!string.Equals(current.Article, article, StringComparison.OrdinalIgnoreCase))
        {
            return new MachineStockResult(
                MachineStockStatuses.WrongArticle,
                warehouse,
                article,
                $"Auf {warehouse} liegt anderes Pulver: {current.Article} ({current.ArticleText}), Charge {current.Batch}, {current.QuantityKg:0.###} kg. Vor dem Nachfüllen ist ein Pulverwechsel erforderlich.",
                DateTimeOffset.UtcNow,
                nonZeroRows);
        }

        return new MachineStockResult(
            MachineStockStatuses.Unique,
            warehouse,
            article,
            $"Eindeutiger Oxaion-Bestand: Charge {current.Batch}, {current.QuantityKg:0.###} kg.",
            DateTimeOffset.UtcNow,
            nonZeroRows);
    }

    public static IReadOnlyList<MachineStockRow> ParseRows(XDocument xml)
    {
        var result = new List<MachineStockRow>();
        foreach (var row in xml.Descendants("ROW"))
        {
            var key = row.Element("KEY");
            if (key is null) continue;

            var warehouse = key.Element("LALAGO")?.Value.Trim() ?? "";
            var article = key.Element("LAIDNR")?.Value.Trim() ?? "";
            var batch = key.Element("LAPONR")?.Value.Trim() ?? row.Element("LLAGEP.LAPONR")?.Value.Trim() ?? "";
            var quantityText = row.Element("LLAWEP.LALABE")?.Value.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(warehouse) || string.IsNullOrWhiteSpace(article) || string.IsNullOrWhiteSpace(batch))
                continue;

            var (quantity, unit) = ParseQuantity(quantityText);
            result.Add(new MachineStockRow(
                warehouse,
                article,
                row.Element("IDNR.TLBEZG")?.Value.Trim() ?? "",
                batch,
                row.Element("PONR.POCHNL")?.Value.Trim() ?? "",
                quantity,
                unit,
                row.Element("LLAWEP.LAYZLBU")?.Value.Trim() ?? ""));
        }
        return result;
    }

    public static (decimal Quantity, string Unit) ParseQuantity(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return (0m, "");
        var trimmed = value.Trim();
        var numeric = new string(trimmed.TakeWhile(ch => char.IsDigit(ch) || ch is '+' or '-' or ',' or '.').ToArray());
        if (string.IsNullOrWhiteSpace(numeric))
            throw new FormatException($"Oxaion stock quantity '{value}' does not start with a numeric value.");

        var normalized = numeric;
        if (normalized.Contains(',') && normalized.Contains('.'))
            normalized = normalized.LastIndexOf(',') > normalized.LastIndexOf('.')
                ? normalized.Replace(".", "").Replace(',', '.')
                : normalized.Replace(",", "");
        else if (normalized.Contains(','))
            normalized = normalized.Replace(',', '.');

        if (!decimal.TryParse(normalized, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var quantity))
            throw new FormatException($"Oxaion stock quantity '{value}' could not be parsed safely.");

        var unit = trimmed[numeric.Length..].Trim();
        return (quantity, unit);
    }

    public static bool HasStop(XDocument xml) => xml.Descendants("STOP").Any();

    private Dictionary<string, string> BuildLaunchContext(string warehouse, string article, string? warehouseText, string? articleText)
    {
        var dta = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["WRKB"] = "", ["DATV"] = "", ["LHKZ20"] = "", ["KSTB"] = "",
            ["TIDF"] = article, ["BWKZBZ"] = "",
            ["STARTUP"] = $"<DUFIRM>{_options.Firm}</DUFIRM><DUIDNV>{article}</DUIDNV><DULAGV>{warehouse}</DULAGV>",
            ["NANW"] = "", ["XLFTBZ"] = "", ["mode"] = "no-attribute-update", ["SNNR20"] = "",
            ["TX_FFMT"] = "Chargen pro Lagerort", ["TX_LAGR"] = "", ["LAGR20"] = "", ["KOBN"] = "",
            ["TX_LAGO"] = warehouseText ?? "", ["DATB"] = "", ["KOKO"] = "0", ["PONR"] = "", ["PONR20"] = "",
            ["ABCK"] = "", ["LHKZBZ"] = "", ["LHKZ"] = "", ["ABCK20"] = "", ["TX_KSTT"] = "",
            ["REPORT"] = "", ["KSTTV"] = "", ["ANWG"] = "LBS", ["KOAW"] = "", ["TX_BUKR"] = "",
            ["FMANWG"] = "LBS", ["BGNR"] = "", ["LHMT20"] = "", ["XLFT"] = "", ["BWKZ"] = "",
            ["B_BBL20"] = "", ["FFMT"] = "CO", ["LAGO20"] = "", ["FFMS"] = "CO", ["LAGR"] = "",
            ["KOPS"] = "0", ["LAPL20"] = "", ["PGMN"] = "LB30230R", ["LAGO"] = warehouse,
            ["KSTTB"] = "", ["TX_TIDF"] = articleText ?? "", ["SNNR"] = "", ["KOVU20"] = "", ["LAPL"] = "",
            ["BWKZ20"] = "", ["TX_WERK"] = "", ["I_TIDF"] = article, ["ABCKBZ"] = "", ["XLFT20"] = "",
            ["WRKV"] = "", ["LHMT"] = "", ["KSTV"] = "", ["BUKR"] = "", ["SSID"] = "",
            ["LHMTBZ"] = "", ["BLNR20"] = "", ["DATE20"] = "", ["BLNR"] = "0"
        };
        return dta;
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
