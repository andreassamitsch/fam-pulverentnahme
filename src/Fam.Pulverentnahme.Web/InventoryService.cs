using System.Globalization;
using System.Xml.Linq;
using Microsoft.Extensions.Options;

namespace Fam.Pulverentnahme.Web;

public sealed class InventoryService
{
    private const string RpFilter = "RP.*";
    private readonly OxaionClient _oxaion;
    private readonly OxaionOptions _options;
    private readonly MachineStockService _machineStock;

    public InventoryService(OxaionClient oxaion, IOptions<OxaionOptions> options, MachineStockService machineStock)
    {
        _oxaion = oxaion;
        _options = options.Value;
        _machineStock = machineStock;
    }

    public async Task<IReadOnlyList<InventoryPosition>> ReadRpStockAsync(CancellationToken ct)
    {
        await using var session = await _oxaion.ConnectAsync(ct);

        // Do not use LB30210R "Chargen je Firma" as article index. For the complete RP.* stock view,
        // start with the already confirmed LB30340R warehouse/batch inquiry and pass the operator-used
        // RP.* article selection directly through the confirmed TIDF/I_TIDF launch context. The returned
        // rows are used only to discover warehouses that currently contain non-zero RP.* stock.
        var warehouseRows = await ReadRpWarehouseRowsAsync(session, ct);
        var warehouses = warehouseRows
            .Where(x => x.QuantityKg != 0m)
            .Select(x => x.Warehouse)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new List<InventoryPosition>();
        foreach (var warehouse in warehouses)
        {
            var warehouseText = await ResolveWarehouseTextAsync(session, warehouse, ct);
            try
            {
                // Lagerplatzgefuehrter Lagerort: read all RP.* batch/bin rows in one list.
                result.AddRange(await ReadRpBinRowsAsync(session, warehouse, warehouseText, ct));
            }
            catch (OxaionRejectedException ex) when (string.Equals(ex.Code, "LAG1626", StringComparison.OrdinalIgnoreCase))
            {
                // Lagerort ohne Lagerplatzorganisation: use the confirmed LB30230R warehouse list.
                // MachineStockService deliberately reads the complete list and exposes all rows in Rows;
                // we only keep RP.* and stock != 0 here.
                var stock = await _machineStock.ReadAsync(session, warehouse, RpFilter, warehouseText, null, ct);
                result.AddRange(stock.Rows
                    .Where(x => x.Article.StartsWith("RP.", StringComparison.OrdinalIgnoreCase) && x.QuantityKg != 0m)
                    .Select(x => new InventoryPosition(
                        x.Article,
                        x.ArticleText,
                        warehouse,
                        warehouseText,
                        "",
                        x.Batch,
                        x.QuantityKg,
                        x.Unit,
                        x.QuantityKg < 0m)));
            }
        }

        return result
            .Where(x => x.Article.StartsWith("RP.", StringComparison.OrdinalIgnoreCase) && x.QuantityKg != 0m)
            .GroupBy(x => InventoryKey(x), StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(x => x.Article, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Warehouse, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.StorageBin, StringComparer.Ordinal)
            .ThenBy(x => x.Batch, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<IReadOnlyList<(string Warehouse, string Article, string Batch, decimal QuantityKg)>> ReadRpWarehouseRowsAsync(
        OxaionSession session,
        CancellationToken ct)
    {
        var context = BuildInquiryContext(RpFilter, "", "", "CL", "Chargen und Lagerorte pro Artikel", "LB30340R");
        var ssid = await LaunchAsync(session, context, "LB30340R", "LB30340", ct);
        var pages = await ReadAllPagesAsync(session, "LB30340R", ssid, ct);
        return pages
            .SelectMany(SourceStockService.ParseArticleWarehouseRows)
            .Where(r => r.Article.StartsWith("RP.", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private async Task<IReadOnlyList<InventoryPosition>> ReadRpBinRowsAsync(
        OxaionSession session,
        string warehouse,
        string warehouseText,
        CancellationToken ct)
    {
        var context = BuildInquiryContext(RpFilter, warehouse, warehouseText, "PT", "Lagerplätze pro Artikel und -ort", "LB30430R");
        var ssid = await LaunchAsync(session, context, "LB30430R", "LB30430", ct);
        var pages = await ReadAllPagesAsync(session, "LB30430R", ssid, ct);

        return pages
            .SelectMany(x => ParseInventoryBinRows(x, warehouseText))
            .Where(x => string.Equals(x.Warehouse, warehouse, StringComparison.OrdinalIgnoreCase))
            .Where(x => x.Article.StartsWith("RP.", StringComparison.OrdinalIgnoreCase))
            .Where(x => x.QuantityKg != 0m)
            .Select(x => new InventoryPosition(
                x.Article,
                x.ArticleText,
                x.Warehouse,
                x.WarehouseText,
                x.StorageBin,
                x.Batch,
                x.QuantityKg,
                x.Unit,
                x.QuantityKg < 0m))
            .ToList();
    }

    internal static IReadOnlyList<InventoryBinRow> ParseInventoryBinRows(XDocument xml, string warehouseText)
    {
        var rows = new List<InventoryBinRow>();
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
            if (string.IsNullOrWhiteSpace(warehouse)
                || string.IsNullOrWhiteSpace(article)
                || string.IsNullOrWhiteSpace(batch))
                continue;

            var (quantity, unit) = MachineStockService.ParseQuantity(quantityText);
            if (string.Equals(unit, "kg", StringComparison.OrdinalIgnoreCase)) unit = "KGM";
            rows.Add(new InventoryBinRow(
                warehouse,
                warehouseText,
                article,
                row.Element("IDNR.TLBEZG")?.Value.Trim() ?? "",
                storageBin,
                batch,
                quantity,
                unit));
        }
        return rows;
    }

    private async Task<string> LaunchAsync(
        OxaionSession session,
        Dictionary<string, string> context,
        string program,
        string noHw,
        CancellationToken ct)
    {
        var launch = await session.CallAsync("US30600J", "", context, ct);
        OxaionSession.AssertNoFcod(launch);
        var ssid = Get(launch.Dta, "SSID");
        if (string.IsNullOrWhiteSpace(ssid))
            throw new InvalidOperationException("US30600J did not return SSID for " + program);

        var hdr = Merge(context, launch.Dta);
        hdr["SSID"] = ssid;
        hdr["NOHWPgm"] = noHw;
        OxaionSession.AssertNoFcod(await session.CallAsync(program, "*GETHDR", hdr, ct));
        return ssid;
    }

    private static async Task<IReadOnlyList<XDocument>> ReadAllPagesAsync(
        OxaionSession session,
        string program,
        string ssid,
        CancellationToken ct)
    {
        var pages = new List<XDocument>();
        var page = await session.CallAsync(program, "*FIRSTLIST", Dict(
            ("FLD", ""), ("SSID", ssid), ("PFLD", ""), ("MC-Modus", ""), ("mode", "reset")), ct);
        OxaionSession.AssertNoFcod(page);

        for (var i = 0; i < 100; i++)
        {
            pages.Add(page.Xml);
            if (MachineStockService.HasStop(page.Xml)) return pages;
            page = await session.CallAsync(program, "*NEXTLIST", Dict(("SSID", ssid)), ct);
            OxaionSession.AssertNoFcod(page);
        }
        throw new InvalidOperationException(program + " did not return STOP within 100 pages.");
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
        string program) =>
        new(StringComparer.Ordinal)
        {
            ["WRKB"]="", ["DATV"]="", ["LHKZ20"]="", ["KSTB"]="", ["TIDF"]=format=="PT"?article:"", ["BWKZBZ"]="", ["NANW"]="", ["TSAKZ"]="",
            ["XLFTBZ"]="", ["mode"]="no-attribute-update", ["SNNR20"]="", ["TX_FFMT"]=formatText, ["TX_LAGR"]="", ["LAGR20"]="", ["KOBN"]="", ["TX_LAGO"]=warehouseText,
            ["DATB"]="", ["KOKO"]="0", ["PONR"]="", ["PONR20"]="", ["ABCK"]="", ["LHKZBZ"]="", ["LHKZ"]="", ["ABCK20"]="", ["TX_KSTT"]="", ["REPORT"]="",
            ["KSTTV"]="", ["ANWG"]="LBS", ["KOAW"]="", ["TX_BUKR"]="", ["INBR"]="", ["FMANWG"]="LBS", ["BGNR"]="", ["LHMT20"]="", ["XLFT"]="", ["BWKZ"]="",
            ["B_BBL20"]="", ["FFMT"]=format, ["LAGO20"]="", ["FFMS"]=format, ["LAGR"]="", ["KOPS"]="0", ["LAPL20"]="", ["PGMN"]=program, ["LAGO"]=warehouse,
            ["KSTTB"]="", ["TX_TIDF"]="", ["SNNR"]="", ["KOVU20"]="", ["BKFM"]="", ["LAPL"]="", ["KEYTYPE"]="P", ["BWKZ20"]="", ["TX_WERK"]="", ["I_TIDF"]=article,
            ["ABCKBZ"]="", ["XLFT20"]="", ["WRKV"]="", ["LHMT"]="", ["KSTV"]="", ["BUKR"]="", ["SSID"]="", ["LHMTBZ"]="", ["BLNR20"]="", ["DATE20"]="", ["BLNR"]="0",
            ["STARTUP"]=$"<DUFIRM>{_options.Firm}</DUFIRM><DUIDNV>{article}</DUIDNV><DULAGV>{warehouse}</DULAGV>", ["NEXTPGM"]=program
        };

    private static string InventoryKey(InventoryPosition x) =>
        $"{x.Article.Trim().ToUpperInvariant()}\u001f{x.Warehouse.Trim().ToUpperInvariant()}\u001f{x.StorageBin.Trim()}\u001f{x.Batch.Trim()}";

    private static Dictionary<string,string> Dict(params (string Key,string Value)[] values) =>
        values.ToDictionary(x => x.Key, x => x.Value ?? "", StringComparer.Ordinal);

    private static Dictionary<string,string> Merge(IReadOnlyDictionary<string,string> a, IReadOnlyDictionary<string,string> b)
    {
        var r = new Dictionary<string,string>(a, StringComparer.Ordinal);
        foreach (var x in b) r[x.Key] = x.Value ?? "";
        return r;
    }

    private static string Get(IReadOnlyDictionary<string,string> d, string k) => d.TryGetValue(k, out var v) ? v : "";
}

public sealed record InventoryBinRow(
    string Warehouse,
    string WarehouseText,
    string Article,
    string ArticleText,
    string StorageBin,
    string Batch,
    decimal QuantityKg,
    string Unit);
