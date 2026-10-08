using System.Xml.Linq;

namespace Fam.Pulverentnahme.Web;

public sealed class ChargeOriginProtocolException(string message) : Exception(message);

public sealed record ChargeOriginRow(
    int Level,
    string Article,
    string Batch,
    string Supplier,
    string PurchaseOrder,
    string DeliveryNote,
    string ProductionOrder,
    string GoodsReceipt,
    string SessionKey,
    string PositionKey,
    bool HasSubtrees);

public sealed record ChargeOriginBaseBatch(
    string Article,
    string Batch,
    string Supplier,
    string PurchaseOrder,
    string DeliveryNote,
    string ProductionOrder,
    string GoodsReceipt);

public sealed record ChargeOriginResult(
    string Article,
    string Batch,
    IReadOnlyList<ChargeOriginBaseBatch> BaseBatches,
    int RowsRead,
    int ExpandedNodes,
    int MaxLevel,
    long? ObjectId);

public sealed class ChargeOriginService
{
    internal const string UsageProgram = "US17490J";
    internal const string OriginProgram = "US17476R";
    internal const int MaxExpandedNodes = 256;
    internal const int MaxRows = 10000;
    internal const int MaxDepth = 64;

    private readonly OxaionClient _oxaion;

    public ChargeOriginService(OxaionClient oxaion)
    {
        _oxaion = oxaion;
    }

    public async Task<ChargeOriginResult> ReadAsync(
        string article,
        string batch,
        long? objectId,
        CancellationToken ct)
    {
        article = NormalizeRequired(article, nameof(article)).ToUpperInvariant();
        batch = NormalizeRequired(batch, nameof(batch)).ToUpperInvariant();
        if (objectId is <= 0)
            throw new ArgumentException("objectId muss positiv sein, wenn er angegeben wird.", nameof(objectId));

        await using var session = await _oxaion.ConnectAsync(ct);

        var entry = BuildEntryDta(article, batch, objectId);

        var load = await session.CallAsync(UsageProgram, "*LOADUSGI", entry, ct);
        OxaionSession.AssertNoFcod(load);

        var usageInput = new Dictionary<string, string>(entry, StringComparer.Ordinal)
        {
            ["TX_USAGE"] = "CH"
        };
        var usage = await session.CallAsync(UsageProgram, "*USGPARAMS", usageInput, ct);
        OxaionSession.AssertNoFcod(usage);

        var program = GetRequired(usage.Dta, "PGMN", "US17490J *USGPARAMS");
        if (!string.Equals(program, OriginProgram, StringComparison.Ordinal))
            throw new ChargeOriginProtocolException(
                $"Oxaion lieferte für Chargenherkunft das unerwartete Programm '{program}' statt '{OriginProgram}'.");

        var ssid = GetRequired(usage.Dta, "SSID", "US17490J *USGPARAMS");

        var headerContext = Merge(entry, usage.Dta);
        headerContext["TX_USAGE"] = "CH";
        headerContext["SSID"] = ssid;
        headerContext["PGMN"] = OriginProgram;
        headerContext["NOHWPgm"] = "US17476";

        var header = await session.CallAsync(OriginProgram, "*GETHDR", headerContext, ct);
        OxaionSession.AssertNoFcod(header);

        var root = await session.CallAsync(OriginProgram, "*FIRSTLIST", new Dictionary<string, string>
        {
            ["FLD"] = "",
            ["SSID"] = ssid,
            ["PFLD"] = "",
            ["MC-Modus"] = "true",
            ["mode"] = "reset"
        }, ct);
        OxaionSession.AssertNoFcod(root);
        EnsureCompleteList(root.Xml, "US17476R *FIRSTLIST root");

        var rows = ParseRows(root.Xml).ToList();
        EnsureRowLimit(rows.Count);

        var queue = new Queue<ChargeOriginRow>(
            rows.Where(IsExpandable));
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var expandedNodes = 0;

        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var node = queue.Dequeue();
            if (node.Level > MaxDepth)
                throw new ChargeOriginProtocolException(
                    $"Chargenherkunft überschreitet die Sicherheitsgrenze von {MaxDepth} Ebenen.");

            var nodeKey = $"{node.SessionKey}\u001f{node.PositionKey}";
            if (!visited.Add(nodeKey))
                continue;

            expandedNodes++;
            if (expandedNodes > MaxExpandedNodes)
                throw new ChargeOriginProtocolException(
                    $"Chargenherkunft überschreitet die Sicherheitsgrenze von {MaxExpandedNodes} aufzuklappenden Knoten.");

            var childInput = BuildExpansionDta(ssid, node, headerContext);
            var child = await session.CallAsync(OriginProgram, "*FIRSTLIST", childInput, ct);
            OxaionSession.AssertNoFcod(child);
            EnsureCompleteList(child.Xml, $"US17476R *FIRSTLIST {node.PositionKey}");

            var childRows = ParseRows(child.Xml);
            rows.AddRange(childRows);
            EnsureRowLimit(rows.Count);

            foreach (var childRow in childRows.Where(IsExpandable))
                queue.Enqueue(childRow);
        }

        var baseBatches = ReduceBaseBatches(rows);
        return new ChargeOriginResult(
            article,
            batch,
            baseBatches,
            rows.Count,
            expandedNodes,
            rows.Count == 0 ? 0 : rows.Max(x => x.Level),
            objectId);
    }

    internal static Dictionary<string, string> BuildEntryDta(
        string article,
        string batch,
        long? objectId)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SEPONR"] = batch,
            ["QHPONR"] = batch,
            ["POPONR"] = batch,
            ["KEYTYPE"] = "UPOST",
            ["SEIDNR"] = article,
            ["FIPOBID"] = "0",
            ["QHIDNR"] = article,
            ["POIDNR"] = article
        };

        // The captured Oxaion transaction contains POOBID/FIOBID, but the trace does not
        // show how that internal UPOST object id is obtained from article + batch.
        // Never invent it. If the caller knows the id, replay it exactly; otherwise omit it
        // and let STAGING prove whether Oxaion can resolve the context without it.
        if (objectId is > 0)
        {
            var value = objectId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            result["POOBID"] = value;
            result["FIOBID"] = value;
        }

        return result;
    }

    internal static Dictionary<string, string> BuildExpansionDta(
        string ssid,
        ChargeOriginRow node,
        IReadOnlyDictionary<string, string> headerContext)
    {
        Dictionary<string, string> result;

        // The captured first expansion sends the full screen context. Deeper expansions
        // are confirmed with only SSID + PESSID + PEMPOS and NoHeader.
        if (node.Level <= 1)
            result = new Dictionary<string, string>(headerContext, StringComparer.Ordinal);
        else
            result = new Dictionary<string, string>(StringComparer.Ordinal);

        result["FLD"] = "";
        result["PFLD"] = "";
        result["SSID"] = ssid;
        result["PESSID"] = node.SessionKey;
        result["PEMPOS"] = node.PositionKey;
        result["NoHeader"] = "true";
        result.Remove("mode");
        return result;
    }

    internal static IReadOnlyList<ChargeOriginRow> ParseRows(XDocument xml)
    {
        var result = new List<ChargeOriginRow>();

        foreach (var row in xml.Descendants("ROW"))
        {
            var key = row.Element("KEY");
            var levelText = Text(row, "UPOVEP.PESTCK");
            _ = int.TryParse(levelText, out var level);

            result.Add(new ChargeOriginRow(
                level,
                NormalizeArticle(Text(row, "UPOVEP.PEIDNR")),
                Text(row, "UPOVEP.PEPONR"),
                Text(row, "UPOVEP.PELINR"),
                Text(row, "UPOVEP.PEBENR"),
                Text(row, "UPOVEP.PELFNR"),
                Text(row, "UPOVEP.PEFAUN"),
                Text(row, "UPOVEP.PEWEGN"),
                Text(key, "PESSID"),
                Text(key, "PEMPOS"),
                string.Equals(
                    row.Attribute("SUBTREES")?.Value,
                    "TRUE",
                    StringComparison.OrdinalIgnoreCase)));
        }

        return result;
    }

    internal static IReadOnlyList<ChargeOriginBaseBatch> ReduceBaseBatches(
        IEnumerable<ChargeOriginRow> rows)
    {
        var materialized = rows
            .Where(x => !string.IsNullOrWhiteSpace(x.Batch))
            .ToList();

        var intermediate = materialized
            .Where(x => x.HasSubtrees)
            .Select(BatchKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return materialized
            .Where(x => !intermediate.Contains(BatchKey(x)))
            .GroupBy(BatchKey, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var best = group
                    .OrderByDescending(MetadataScore)
                    .ThenByDescending(x => x.Level)
                    .First();

                return new ChargeOriginBaseBatch(
                    best.Article,
                    best.Batch,
                    best.Supplier,
                    best.PurchaseOrder,
                    best.DeliveryNote,
                    best.ProductionOrder,
                    best.GoodsReceipt);
            })
            .OrderBy(x => x.Article, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Batch, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static void EnsureCompleteList(XDocument xml, string stage)
    {
        if (!xml.Descendants("STOP").Any())
            throw new ChargeOriginProtocolException(
                $"{stage} lieferte keinen STOP-Marker. Der Herkunftsbaum könnte unvollständig sein; Ausgabe wird verworfen.");
    }

    private static bool IsExpandable(ChargeOriginRow row) =>
        row.HasSubtrees
        && !string.IsNullOrWhiteSpace(row.SessionKey)
        && !string.IsNullOrWhiteSpace(row.PositionKey);

    private static void EnsureRowLimit(int count)
    {
        if (count > MaxRows)
            throw new ChargeOriginProtocolException(
                $"Chargenherkunft überschreitet die Sicherheitsgrenze von {MaxRows} Ergebniszeilen.");
    }

    private static string BatchKey(ChargeOriginRow row) =>
        $"{row.Article}\u001f{row.Batch}";

    private static int MetadataScore(ChargeOriginRow row) =>
        Score(row.Supplier)
        + Score(row.PurchaseOrder)
        + Score(row.DeliveryNote)
        + Score(row.ProductionOrder)
        + Score(row.GoodsReceipt);

    private static int Score(string value) => string.IsNullOrWhiteSpace(value) ? 0 : 1;

    private static string NormalizeArticle(string value)
    {
        var parts = (value ?? "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "" : parts[0].Trim();
    }

    private static string NormalizeRequired(string? value, string parameterName)
    {
        var normalized = (value ?? "").Trim();
        if (normalized.Length == 0)
            throw new ArgumentException($"{parameterName} ist erforderlich.", parameterName);
        return normalized;
    }

    private static string Text(XElement? parent, string name) =>
        parent?.Element(name)?.Value?.Trim() ?? "";

    private static string GetRequired(
        IReadOnlyDictionary<string, string> values,
        string key,
        string stage)
    {
        if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value.Trim();

        throw new ChargeOriginProtocolException(
            $"{stage} lieferte das Pflichtfeld {key} nicht.");
    }

    private static Dictionary<string, string> Merge(
        IReadOnlyDictionary<string, string> first,
        IReadOnlyDictionary<string, string> second)
    {
        var result = new Dictionary<string, string>(first, StringComparer.Ordinal);
        foreach (var pair in second)
            result[pair.Key] = pair.Value ?? "";
        return result;
    }
}
