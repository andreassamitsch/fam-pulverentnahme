using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Fam.Pulverentnahme.Web;

public static class ArticleRecognitionColorStatuses
{
    public const string Complete = "COMPLETE";
    public const string Incomplete = "INCOMPLETE";
    public const string Unavailable = "UNAVAILABLE";
}

public sealed record ArticleRecognitionColor(
    string Feature,
    string Hex,
    string Name);

public sealed record ArticleRecognitionColorsResult(
    string Status,
    string Article,
    ArticleRecognitionColor? Color1,
    ArticleRecognitionColor? Color2,
    string Message);

/// <summary>
/// Read-only article characteristic lookup for the visual recognition colors EFA01/EFA02.
///
/// Confirmed by the 2026-09-03 Oxaion capture for RP.00010:
/// US17000J *SAVKEY -> US17000J *PROPERTY -> US21001J *LOAD
/// -> US21000R *GETHDR -> US21000R *FIRSTLIST.
///
/// Relevant list fields:
/// - UYASMP.ASSMMN : characteristic name (EFA01/EFA02)
/// - UYASMP.ASSMMA : characteristic value containing the six-digit RGB hex value
/// - _INTERN.SMMABZ : characteristic value description, e.g. Schwarz/Violett
///
/// Only the captured FIRSTLIST path is used. If the list does not contain STOP, the lookup fails
/// rather than inventing an unconfirmed pagination call for this Oxaion program.
/// </summary>
public static partial class ArticleRecognitionColorLookup
{
    private const string Feature1 = "EFA01";
    private const string Feature2 = "EFA02";

    public static async Task<ArticleRecognitionColorsResult> ReadAsync(
        OxaionSession session,
        string article,
        CancellationToken ct)
    {
        article = (article ?? "").Trim();
        if (string.IsNullOrWhiteSpace(article))
            throw new ArgumentException("Article is required for recognition-color lookup.");

        var saved = await session.CallAsync("US17000J", "*SAVKEY", Dict(
            ("TLIDNR", article),
            ("PGMN", "US21000"),
            ("KEYTYPE", "UTLSM"),
            ("state", "ANZEIGEN")), ct);
        OxaionSession.AssertNoFcod(saved);

        var copyFromSsid = Get(saved.Dta, "SSID");
        if (string.IsNullOrWhiteSpace(copyFromSsid))
            throw new InvalidOperationException("US17000J *SAVKEY did not return an SSID for article characteristics.");

        var property = await session.CallAsync("US17000J", "*PROPERTY", Dict(
            ("TLIDNR", article),
            ("CPY-FRSSID", copyFromSsid),
            ("SSID", session.SessionId),
            ("PGMN", "US21000"),
            ("KEYTYPE", "UTLSM"),
            ("state", "ANZEIGEN")), ct);
        OxaionSession.AssertNoFcod(property);

        var listSsid = Get(property.Dta, "SSID");
        if (string.IsNullOrWhiteSpace(listSsid))
            throw new InvalidOperationException("US17000J *PROPERTY did not return an SSID for US21000R.");

        var load = await session.CallAsync("US21001J", "*LOAD", Dict(
            ("NOHWPgm", "US210002"),
            ("SSID", listSsid),
            ("state", "ANZEIGEN"),
            ("mode", "merge")), ct);
        OxaionSession.AssertNoFcod(load);

        var header = await session.CallAsync("US21000R", "*GETHDR", Dict(
            ("FLD", ""),
            ("CPY-FRSSID", copyFromSsid),
            ("SSID", listSsid),
            ("PFLD", "")), ct);
        OxaionSession.AssertNoFcod(header);

        var firstList = await session.CallAsync("US21000R", "*FIRSTLIST", Dict(
            ("SSID", listSsid),
            ("mode", "replace")), ct);
        OxaionSession.AssertNoFcod(firstList);

        if (!MachineStockService.HasStop(firstList.Xml))
            throw new InvalidOperationException("US21000R *FIRSTLIST did not return STOP. The confirmed characteristic lookup is incomplete; no unconfirmed pagination call is attempted.");

        return Parse(firstList.Xml, article);
    }

    public static ArticleRecognitionColorsResult Parse(XDocument xml, string article)
    {
        article = (article ?? "").Trim();
        ArticleRecognitionColor? color1 = null;
        ArticleRecognitionColor? color2 = null;
        var invalid = new List<string>();

        foreach (var row in xml.Descendants("ROW"))
        {
            var feature = row.Element("UYASMP.ASSMMN")?.Value.Trim()
                          ?? row.Element("KEY")?.Element("ASSMMN")?.Value.Trim()
                          ?? "";
            if (!string.Equals(feature, Feature1, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(feature, Feature2, StringComparison.OrdinalIgnoreCase))
                continue;

            var rawHex = row.Element("UYASMP.ASSMMA")?.Value.Trim() ?? "";
            var name = row.Element("_INTERN.SMMABZ")?.Value.Trim() ?? "";
            var hex = NormalizeHex(rawHex);
            if (hex is null)
            {
                invalid.Add($"{feature}='{rawHex}'");
                continue;
            }

            var item = new ArticleRecognitionColor(feature.ToUpperInvariant(), hex, name);
            if (string.Equals(feature, Feature1, StringComparison.OrdinalIgnoreCase)) color1 = item;
            else color2 = item;
        }

        if (color1 is not null && color2 is not null)
        {
            return new ArticleRecognitionColorsResult(
                ArticleRecognitionColorStatuses.Complete,
                article,
                color1,
                color2,
                $"Erkennungsfarben {Feature1} und {Feature2} wurden aus den Oxaion-Sachmerkmalen gelesen.");
        }

        var missing = new List<string>();
        if (color1 is null) missing.Add(Feature1);
        if (color2 is null) missing.Add(Feature2);
        var invalidSuffix = invalid.Count == 0 ? "" : $" Ungültige HEX-Ausprägung: {string.Join(", ", invalid)}.";
        return new ArticleRecognitionColorsResult(
            ArticleRecognitionColorStatuses.Incomplete,
            article,
            color1,
            color2,
            $"Erkennungsfarben sind nicht vollständig gepflegt. Fehlend/ungültig: {string.Join(", ", missing)}.{invalidSuffix}");
    }

    public static string? NormalizeHex(string value)
    {
        var normalized = (value ?? "").Trim();
        if (normalized.StartsWith('#')) normalized = normalized[1..];
        return HexColorRegex().IsMatch(normalized) ? normalized.ToUpperInvariant() : null;
    }

    private static string Get(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) ? value : "";

    private static Dictionary<string, string> Dict(params (string Key, string Value)[] values) =>
        values.ToDictionary(x => x.Key, x => x.Value ?? "", StringComparer.Ordinal);

    [GeneratedRegex("^[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColorRegex();
}
