using System.Globalization;
using System.Xml.Linq;

namespace Fam.Pulverentnahme.Web;

public sealed class ChargeOriginService
{
    internal const int MaxExpandedNodes = 250;
    private readonly OxaionClient _oxaion;
    private readonly ILogger<ChargeOriginService> _logger;

    public ChargeOriginService(OxaionClient oxaion, ILogger<ChargeOriginService> logger)
    {
        _oxaion = oxaion;
        _logger = logger;
    }

    public async Task<ChargeOriginResult> ReadBaseBatchesAsync(
        string article,
        string batch,
        long? objectId,
        CancellationToken ct)
    {
        article = NormalizeRequired(article, nameof(article), 22);
        batch = NormalizeRequired(batch, nameof(batch), 30);
        if (objectId is < 0)
            throw new ArgumentException("Oxaion objectId must not be negative.", nameof(objectId));

        await using var session = await _oxaion.ConnectAsync(ct);

        // Captured Oxaion flow 2026-10-08:
        // US17490J *USGPARAMS TX_USAGE=CH -> US17476R *GETHDR -> *FIRSTLIST.
        // POOBID/FIOBID are caller UI context in the trace. Their interface-side lookup is not
        // confirmed yet, therefore zero is used unless a diagnostic STAGING override is supplied.
        var usageContext = BuildUsageContext(article, batch, objectId ?? 0L);
        var usage = await session.CallAsync("US17490J", "*USGPARAMS", usageContext, ct);
        OxaionSession.AssertNoFcod(usage);

        var ssid = Get(usage.Dta, "SSID");
        var program = Get(usage.Dta, "PGMN");
        var application = Get(usage.Dta, "ANWG");
        if (string.IsNullOrWhiteSpace(ssid))
            throw new InvalidOperationException("US17490J *USGPARAMS did not return SSID.");
        if (!string.Equals(program, "US17476R", StringComparison.Ordinal))
            throw new InvalidOperationException($"Oxaion charge origin returned unexpected program '{program}'. Expected US17476R.");
        if (!string.Equals(application, "UST", StringComparison.Ordinal))
            throw new InvalidOperationException($"Oxaion charge origin returned unexpected application '{application}'. Expected UST.");

        var headerContext = Merge(usageContext, usage.Dta);
        Set(headerContext,
            ("MTYPE", "*PGM"),
            ("keyFirm", "*NONE"),
            ("MC-Modus", "true"),
            ("keyFields", "PESSID PEMPOS"),
            ("plainFields", ""),
            ("PGMN", "US17476R"),
            ("KEYTYPE", "UPOST"),
            ("NOHWPgm", "US17476"),
            ("ANWG", "UST"));

        var header = await session.CallAsync("US17476R", "*GETHDR", headerContext, ct);
        OxaionSession.AssertNoFcod(header);

        var first = await session.CallAsync("US17476R", "*FIRSTLIST", Dict(
            ("FLD", ""),
            ("SSID", ssid),
            ("PFLD", ""),
            ("MC-Modus", "true"),
            ("mode", "reset")), ct);
        OxaionSession.AssertNoFcod(first);
        EnsureCompletePage(first.Xml);

        var rows = new List<ChargeOriginRow>(ParseRows(first.Xml));
        var queue = new Queue<ChargeOriginRow>(rows.Where(x => x.HasSubtrees));
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var expandedNodes = 0;

        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var node = queue.Dequeue();
            var visitKey = node.Pessid + "|" + node.Pempos;
            if (!visited.Add(visitKey))
                continue;

            expandedNodes++;
            if (expandedNodes > MaxExpandedNodes)
                throw new InvalidOperationException($"Oxaion charge origin exceeded the safety limit of {MaxExpandedNodes} expandable nodes.");

            IReadOnlyDictionary<string, string> input;
            if (!node.Pempos.Contains('.', StringComparison.Ordinal))
            {
                // The first tree expansion in the captured transaction repeats the UPOST/header
                // context and adds the tree key.
                var firstChild = Merge(headerContext, Dict(
                    ("FLD", ""),
                    ("PFLD", ""),
                    ("PEMPOS", node.Pempos),
                    ("PESSID", node.Pessid),
                    ("NoHeader", "true")));
                input = firstChild;
            }
            else
            {
                // Deeper captured expansions need only the tree session/key.
                input = Dict(
                    ("FLD", ""),
                    ("PEMPOS", node.Pempos),
                    ("SSID", ssid),
                    ("PFLD", ""),
                    ("NoHeader", "true"),
                    ("PESSID", node.Pessid));
            }

            var child = await session.CallAsync("US17476R", "*FIRSTLIST", input, ct);
            OxaionSession.AssertNoFcod(child);
            EnsureCompletePage(child.Xml);

            var childRows = ParseRows(child.Xml);
            rows.AddRange(childRows);
            foreach (var expandable in childRows.Where(x => x.HasSubtrees))
                queue.Enqueue(expandable);
        }

        var baseBatches = SelectBaseBatches(rows);
        _logger.LogInformation(
            "Oxaion charge origin read for {Article}/{Batch}: {Rows} rows, {ExpandedNodes} expanded nodes, {BaseBatches} unique base batches.",
            article,
            batch,
            rows.Count,
            expandedNodes,
            baseBatches.Count);

        return new ChargeOriginResult(article, batch, baseBatches, rows.Count, expandedNodes);
    }

    internal static Dictionary<string, string> BuildUsageContext(string article, string batch, long objectId)
    {
        var oid = objectId.ToString(CultureInfo.InvariantCulture);
        return Dict(
            ("TX_USAGE", "CH"),
            ("POOBID", oid),
            ("SEPONR", batch),
            ("QHPONR", batch),
            ("FIOBID", oid),
            ("POPONR", batch),
            ("KEYTYPE", "UPOST"),
            ("SEIDNR", article),
            ("FIPOBID", "0"),
            ("QHIDNR", article),
            ("POIDNR", article));
    }

    internal static IReadOnlyList<ChargeOriginRow> ParseRows(XDocument xml)
    {
        var result = new List<ChargeOriginRow>();
        foreach (var row in xml.Descendants("ROW"))
        {
            var key = row.Element("KEY");
            var pessid = Text(key?.Element("PESSID"));
            var pempos = Text(key?.Element("PEMPOS"));
            var batch = Text(row.Element("UPOVEP.PEPONR"));
            var article = NormalizeArticleField(Text(row.Element("UPOVEP.PEIDNR")));
            if (string.IsNullOrWhiteSpace(pessid) || string.IsNullOrWhiteSpace(pempos))
                continue;

            _ = int.TryParse(
                Text(row.Element("UPOVEP.PESTCK")),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var level);

            result.Add(new ChargeOriginRow(
                pessid,
                pempos,
                string.Equals((string?)row.Attribute("SUBTREES"), "TRUE", StringComparison.OrdinalIgnoreCase),
                level,
                article,
                batch,
                Text(row.Element("UPOVEP.PELINR")),
                Text(row.Element("UPOVEP.PEBENR")),
                Text(row.Element("UPOVEP.PELFNR")),
                Text(row.Element("UPOVEP.PEFAUN")),
                Text(row.Element("UPOVEP.PEWEGN"))));
        }

        return result;
    }

    internal static IReadOnlyList<ChargeOriginBaseBatch> SelectBaseBatches(IEnumerable<ChargeOriginRow> source)
    {
        var rows = source
            .Where(x => !string.IsNullOrWhiteSpace(x.Article) && !string.IsNullOrWhiteSpace(x.Batch))
            .ToList();

        var expandable = rows
            .Where(x => x.HasSubtrees)
            .Select(Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return rows
            .Where(x => !expandable.Contains(Key(x)))
            .GroupBy(Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                return new ChargeOriginBaseBatch(
                    first.Article,
                    first.Batch,
                    FirstNonEmpty(group.Select(x => x.Supplier)),
                    FirstNonEmpty(group.Select(x => x.PurchaseOrder)),
                    FirstNonEmpty(group.Select(x => x.DeliveryNote)),
                    FirstNonEmpty(group.Select(x => x.GoodsReceipt)),
                    group.Min(x => x.Level));
            })
            .OrderBy(x => x.Article, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Batch, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static void EnsureCompletePage(XDocument xml)
    {
        if (!xml.Descendants("STOP").Any())
            throw new InvalidOperationException(
                "Oxaion charge origin response has no STOP marker. Pagination for US17476R subtree responses is not confirmed; refusing to return an incomplete origin result.");
    }

    internal static string NormalizeArticleField(string raw)
    {
        raw = raw.Trim();
        if (raw.Length > 22)
        {
            var article = raw[..22].Trim();
            if (!string.IsNullOrWhiteSpace(article))
                return article;
        }

        var firstWhitespace = raw.IndexOfAny([' ', '\t', '\r', '\n']);
        return firstWhitespace > 0 ? raw[..firstWhitespace].Trim() : raw;
    }

    private static string NormalizeRequired(string? value, string name, int maxLength)
    {
        var normalized = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException($"{name} is required.", name);
        if (normalized.Length > maxLength)
            throw new ArgumentException($"{name} exceeds the confirmed Oxaion field length {maxLength}.", name);
        return normalized;
    }

    private static string Key(ChargeOriginRow row) => row.Article + "\u001F" + row.Batch;

    private static string FirstNonEmpty(IEnumerable<string> values) =>
        values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";

    private static string Text(XElement? element) => (element?.Value ?? "").Trim();

    private static string Get(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) ? (value ?? "").Trim() : "";

    private static Dictionary<string, string> Dict(params (string Key, string Value)[] values) =>
        values.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);

    private static Dictionary<string, string> Merge(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right)
    {
        var result = new Dictionary<string, string>(left, StringComparer.Ordinal);
        foreach (var pair in right)
            result[pair.Key] = pair.Value ?? "";
        return result;
    }

    private static void Set(Dictionary<string, string> target, params (string Key, string Value)[] values)
    {
        foreach (var (key, value) in values)
            target[key] = value;
    }
}

internal sealed record ChargeOriginRow(
    string Pessid,
    string Pempos,
    bool HasSubtrees,
    int Level,
    string Article,
    string Batch,
    string Supplier,
    string PurchaseOrder,
    string DeliveryNote,
    string ProductionOrder,
    string GoodsReceipt);

public sealed record ChargeOriginBaseBatch(
    string Article,
    string Batch,
    string Supplier,
    string PurchaseOrder,
    string DeliveryNote,
    string GoodsReceipt,
    int FirstLevel);

public sealed record ChargeOriginResult(
    string Article,
    string Batch,
    IReadOnlyList<ChargeOriginBaseBatch> BaseBatches,
    int RowsRead,
    int ExpandedNodes);
