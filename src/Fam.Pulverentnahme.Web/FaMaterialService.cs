using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Options;

namespace Fam.Pulverentnahme.Web;

public sealed class FaMaterialService
{
    private readonly OxaionClient _oxaion;
    private readonly OxaionOptions _options;

    public FaMaterialService(OxaionClient oxaion, IOptions<OxaionOptions> options)
    { _oxaion = oxaion; _options = options.Value; }

    public async Task<FaMaterialPositionResult> FindUniqueAsync(string orderNo, string article, CancellationToken ct)
    {
        await using var session = await _oxaion.ConnectAsync(ct);
        return await FindUniqueAsync(session, orderNo, article, ct);
    }

    internal async Task<FaMaterialPositionResult> FindUniqueAsync(OxaionSession session, string orderNo, string article, CancellationToken ct)
    {
        orderNo = (orderNo ?? "").Trim(); article = (article ?? "").Trim();
        if (orderNo.Length == 0 || article.Length == 0) throw new ArgumentException("Fertigungsauftrag und Artikel sind erforderlich.");
        var initial = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) + Random.Shared.Next(100000, 999999).ToString(CultureInfo.InvariantCulture);
        var save = await session.CallAsync("PW20200", "*SAVSLT4CS", Dict(
            ("ISSID", initial), ("SSID", initial), ("AMPOSN", "0"), ("KEYTYPE", "C_PWAMA"),
            ("AMFAUN", orderNo), ("AMIDNK", "")), ct);
        OxaionSession.AssertNoFcod(save);
        var ssid = Get(save.Dta, "SSID");
        if (string.IsNullOrWhiteSpace(ssid)) throw new InvalidOperationException("PW20200 *SAVSLT4CS did not return SSID.");
        var cpy = Get(save.Dta, "CPY-FRSSID"); if (string.IsNullOrWhiteSpace(cpy)) cpy = initial;

        var keyInput = Merge(save.Dta, Dict(
            ("SSID", ssid), ("AMPOSN", "0"), ("PGMN", "PW20200"), ("AMIDNK", ""),
            ("KEYTYPE", "C_PWAMA"), ("ISSID", initial), ("init-filter", "false"),
            ("CPY-FRSSID", cpy), ("AMFAUN", orderNo)));
        var key = await session.CallAsync("PW20090J", "*SAVKEY", keyInput, ct);
        OxaionSession.AssertNoFcod(key);
        if (!string.IsNullOrWhiteSpace(Get(key.Dta, "SSID"))) ssid = Get(key.Dta, "SSID");

        var hdrInput = Merge(keyInput, key.Dta);
        foreach (var pair in Dict(
                     ("SSID", ssid), ("AMPOSN", "0"), ("PGMN", "PW20200"), ("AMIDNK", ""),
                     ("MODE", "XX18"), ("NOHWPgm", "PW20200R"), ("mode", "merge-attributes"),
                     ("KEYTYPE", "C_PWAMA"), ("ISSID", initial), ("init-filter", "false"),
                     ("FLD", ""), ("CPY-FRSSID", cpy), ("PFLD", ""), ("AMFAUN", orderNo))) hdrInput[pair.Key] = pair.Value;
        OxaionSession.AssertNoFcod(await session.CallAsync("PW20200R", "*GETHDR", hdrInput, ct));
        var list = await session.CallAsync("PW20200R", "*FIRSTLIST", Dict(("FLD", ""), ("SSID", ssid), ("PFLD", ""), ("mode", "replace")), ct);
        OxaionSession.AssertNoFcod(list);
        if (!list.Xml.Descendants("STOP").Any()) throw new InvalidOperationException("PW20200R material list did not return STOP; incomplete result is not accepted.");

        var matches = ParseMaterialList(list.Xml, orderNo, article);
        if (matches.Count == 0) throw new ProcessConflictException($"Im Fertigungsauftrag {orderNo} wurde keine Materialposition für {article} gefunden.");
        if (matches.Count > 1) throw new ProcessConflictException($"Im Fertigungsauftrag {orderNo} wurden {matches.Count} Materialpositionen für {article} gefunden. Keine automatische Auswahl zulässig.");
        var m = matches[0];

        var readInput = Merge(hdrInput, Dict(
            ("SSID", ssid), ("AMPOSN", m.Position.ToString(CultureInfo.InvariantCulture)), ("PGMN", "PW20200"),
            ("AMIDNK", m.Article), ("MODE", "XX18"), ("NOHWPgm", "PW20200R"), ("mode", "merge-attributes"),
            ("KEYTYPE", "C_PWAMA"), ("ISSID", initial), ("init-filter", "false"), ("FLD", ""),
            ("AMFIRM", _options.Firm), ("CPY-FRSSID", cpy), ("PFLD", ""), ("AMFAUN", orderNo)));
        var read = await session.CallAsync("PW20201J", "*READ", readInput, ct);
        OxaionSession.AssertNoFcod(read);
        return ParseMaterialRead(read.Xml, orderNo, m.Position, article, m.ArticleText);
    }

    internal static IReadOnlyList<(int Position, string Article, string ArticleText)> ParseMaterialList(XDocument xml, string orderNo, string article)
    {
        var result = new List<(int, string, string)>();
        foreach (var row in xml.Descendants("ROW"))
        {
            var key = row.Element("KEY"); if (key is null) continue;
            var fa = key.Element("AMFAUN")?.Value.Trim() ?? "";
            var id = key.Element("AMIDNK")?.Value.Trim() ?? "";
            var posText = key.Element("AMPOSN")?.Value.Trim() ?? "";
            if (!string.Equals(fa, orderNo, StringComparison.Ordinal) || !string.Equals(id, article, StringComparison.OrdinalIgnoreCase)) continue;
            if (!int.TryParse(posText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pos)) continue;
            var text = row.Element("AMIDNK_TLST.TLBEZG")?.Value.Trim() ?? "";
            result.Add((pos, id, text));
        }
        return result;
    }

    internal static FaMaterialPositionResult ParseMaterialRead(XDocument xml, string orderNo, int position, string article, string fallbackText)
    {
        foreach (var dta in xml.Descendants("DTA"))
        {
            string V(string n) => dta.Element(n)?.Value.Trim() ?? "";
            if (!string.Equals(V("AMFAUN"), orderNo, StringComparison.Ordinal)
                || !string.Equals(V("AMIDNK"), article, StringComparison.OrdinalIgnoreCase)
                || !int.TryParse(V("AMPOSN"), out var p) || p != position) continue;
            var status = int.TryParse(V("AMMPST"), out var s) ? s : -1;
            return new FaMaterialPositionResult(
                orderNo, position, article,
                string.IsNullOrWhiteSpace(V("TX_IDNK02")) ? fallbackText : V("TX_IDNK02"),
                ParseQty(V("AMMATB")), ParseQty(V("AMMATV")), V("AMMEKZ"), status, V("TX_MPST"),
                MkStatusAllowed(status), DateTimeOffset.UtcNow);
        }
        throw new InvalidOperationException("PW20201J *READ did not contain the exact requested material position.");
    }

    public static bool MkStatusAllowed(int status) => status is 0 or 1 or 8;

    // The second value is the operator-entered actual total consumption. It is not added to AMMATV.
    public static decimal TargetConsumed(decimal alreadyConsumed, decimal actualConsumption) => actualConsumption;

    internal static decimal ParseQty(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0m;
        var x = value.Trim();
        if (x.Contains(',') && x.Contains('.')) x = x.LastIndexOf(',') > x.LastIndexOf('.') ? x.Replace(".", "").Replace(',', '.') : x.Replace(",", "");
        else x = x.Replace(',', '.');
        if (decimal.TryParse(x, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v)) return v;
        throw new FormatException("Invalid Oxaion material quantity: " + value);
    }

    private static Dictionary<string, string> Dict(params (string Key, string Value)[] values) => values.ToDictionary(x => x.Key, x => x.Value ?? "", StringComparer.Ordinal);
    private static Dictionary<string, string> Merge(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b)
    { var r = new Dictionary<string, string>(a, StringComparer.Ordinal); foreach (var x in b) r[x.Key] = x.Value ?? ""; return r; }
    private static string Get(IReadOnlyDictionary<string, string> d, string k) => d.TryGetValue(k, out var v) ? v : "";
}
