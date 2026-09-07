using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Options;

namespace Fam.Pulverentnahme.Web;

public sealed class InventoryService
{
    private readonly OxaionClient _oxaion;
    private readonly OxaionOptions _options;
    private readonly MachineStockService _machineStock;

    public InventoryService(OxaionClient oxaion, IOptions<OxaionOptions> options, MachineStockService machineStock)
    { _oxaion = oxaion; _options = options.Value; _machineStock = machineStock; }

    public async Task<IReadOnlyList<InventoryPosition>> ReadRpStockAsync(CancellationToken ct)
    {
        await using var session = await _oxaion.ConnectAsync(ct);
        var articles = await ReadRpArticleIndexAsync(session, ct);
        var result = new List<InventoryPosition>();
        foreach (var a in articles)
            result.AddRange(await ReadArticleAsync(session, a.Article, a.ArticleText, ct));
        return result
            .Where(x => x.QuantityKg != 0m)
            .OrderBy(x => x.Article, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Warehouse, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.StorageBin, StringComparer.Ordinal)
            .ThenBy(x => x.Batch, StringComparer.Ordinal)
            .ToList();
    }

    internal async Task<IReadOnlyList<(string Article, string ArticleText)>> ReadRpArticleIndexAsync(OxaionSession session, CancellationToken ct)
    {
        var command = await session.CallAsync("MN10209J", "*CHKCMD", Dict(("CHKCMD", "CF"), ("_father_", "CMDLINE")), ct);
        OxaionSession.AssertNoFcod(command);
        var ssid = Get(command.Dta, "SSID");
        if (string.IsNullOrWhiteSpace(ssid)) throw new InvalidOperationException("MN10209J *CHKCMD CF did not return SSID for LB30210R.");
        var list = await session.CallAsync("LB30210R", "*FIRSTLIST", Dict(
            ("FLD", ""), ("SSID", ssid), ("PFLD", ""), ("MC-Modus", ""), ("mode", "replace")), ct);
        OxaionSession.AssertNoFcod(list);
        if (!list.Xml.Descendants("STOP").Any())
            throw new InvalidOperationException("LB30210R article index did not return STOP in the confirmed FIRSTLIST. Pagination is not inferred for this index.");
        return ParseRpArticleIndex(list.Xml);
    }

    internal static IReadOnlyList<(string Article, string ArticleText)> ParseRpArticleIndex(XDocument xml)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in xml.Descendants("ROW"))
        {
            var key = row.Element("KEY");
            var article = key?.Element("POIDNR")?.Value.Trim() ?? "";
            if (!article.StartsWith("RP.", StringComparison.OrdinalIgnoreCase)) continue;
            var text = row.Element("IDNR.TLBEZG")?.Value.Trim() ?? "";
            if (!result.ContainsKey(article)) result[article] = text;
        }
        return result.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x => (x.Key, x.Value)).ToList();
    }

    private async Task<IReadOnlyList<InventoryPosition>> ReadArticleAsync(OxaionSession session, string article, string articleText, CancellationToken ct)
    {
        var warehouseRows = await ReadWarehouseRowsAsync(session, article, ct);
        var result = new List<InventoryPosition>();
        foreach (var warehouse in warehouseRows.Select(x => x.Warehouse).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var text = await ResolveWarehouseTextAsync(session, warehouse, ct);
            try
            {
                var positions = await ReadBinRowsAsync(session, article, warehouse, text, ct);
                result.AddRange(positions.Where(x => x.QuantityKg != 0m).Select(x => new InventoryPosition(
                    article, articleText, x.Warehouse, x.WarehouseText, x.StorageBin, x.Batch, x.QuantityKg, x.Unit, x.QuantityKg < 0m)));
            }
            catch (OxaionRejectedException ex) when (string.Equals(ex.Code, "LAG1626", StringComparison.OrdinalIgnoreCase))
            {
                var stock = await _machineStock.ReadAsync(session, warehouse, article, text, articleText, ct);
                result.AddRange(stock.Rows
                    .Where(x => string.Equals(x.Article, article, StringComparison.OrdinalIgnoreCase) && x.QuantityKg != 0m)
                    .Select(x => new InventoryPosition(article, string.IsNullOrWhiteSpace(x.ArticleText) ? articleText : x.ArticleText,
                        warehouse, text, "", x.Batch, x.QuantityKg, x.Unit, x.QuantityKg < 0m)));
            }
        }
        return result;
    }

    private async Task<IReadOnlyList<(string Warehouse, string Batch, decimal QuantityKg)>> ReadWarehouseRowsAsync(OxaionSession session, string article, CancellationToken ct)
    {
        var context = BuildInquiryContext(article, "", "", "CL", "Chargen und Lagerorte pro Artikel", "LB30340R");
        var ssid = await LaunchAsync(session, context, "LB30340R", "LB30340", ct);
        var pages = await ReadAllPagesAsync(session, "LB30340R", ssid, ct);
        return pages.SelectMany(SourceStockService.ParseArticleWarehouseRows)
            .Where(r => string.Equals(r.Article, article, StringComparison.OrdinalIgnoreCase))
            .Select(r => (r.Warehouse, r.Batch, r.QuantityKg)).ToList();
    }

    private async Task<IReadOnlyList<SourceStockPosition>> ReadBinRowsAsync(OxaionSession session, string article, string warehouse, string text, CancellationToken ct)
    {
        var context = BuildInquiryContext(article, warehouse, text, "PT", "Lagerplätze pro Artikel und -ort", "LB30430R");
        var ssid = await LaunchAsync(session, context, "LB30430R", "LB30430", ct);
        var pages = await ReadAllPagesAsync(session, "LB30430R", ssid, ct);
        return pages.SelectMany(x => SourceStockService.ParsePositionRows(x, text))
            .Where(x => string.Equals(x.Warehouse, warehouse, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private async Task<string> LaunchAsync(OxaionSession session, Dictionary<string, string> context, string program, string noHw, CancellationToken ct)
    {
        var launch = await session.CallAsync("US30600J", "", context, ct); OxaionSession.AssertNoFcod(launch);
        var ssid = Get(launch.Dta, "SSID"); if (string.IsNullOrWhiteSpace(ssid)) throw new InvalidOperationException("US30600J did not return SSID for " + program);
        var hdr = Merge(context, launch.Dta); hdr["SSID"] = ssid; hdr["NOHWPgm"] = noHw;
        OxaionSession.AssertNoFcod(await session.CallAsync(program, "*GETHDR", hdr, ct));
        return ssid;
    }

    private static async Task<IReadOnlyList<XDocument>> ReadAllPagesAsync(OxaionSession session, string program, string ssid, CancellationToken ct)
    {
        var pages = new List<XDocument>();
        var page = await session.CallAsync(program, "*FIRSTLIST", Dict(("FLD", ""), ("SSID", ssid), ("PFLD", ""), ("MC-Modus", ""), ("mode", "reset")), ct);
        OxaionSession.AssertNoFcod(page);
        for (var i = 0; i < 100; i++)
        {
            pages.Add(page.Xml); if (MachineStockService.HasStop(page.Xml)) return pages;
            page = await session.CallAsync(program, "*NEXTLIST", Dict(("SSID", ssid)), ct); OxaionSession.AssertNoFcod(page);
        }
        throw new InvalidOperationException(program + " did not return STOP within 100 pages.");
    }

    private async Task<string> ResolveWarehouseTextAsync(OxaionSession session, string warehouse, CancellationToken ct)
    {
        var plain = await session.CallAsync("US00006J", "*GETPLAIN", Dict(("MFLD", "LAGO"), ("PGMN", "US30600J"),
            ("LAGO", warehouse), ("PFIELD", "TX_LAGO"), ("FIELD", "LAGO")), ct);
        OxaionSession.AssertNoFcod(plain);
        var text = Get(plain.Dta, "TX_LAGO"); return string.IsNullOrWhiteSpace(text) ? warehouse : text;
    }

    private Dictionary<string, string> BuildInquiryContext(string article, string warehouse, string warehouseText, string format, string formatText, string program) =>
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

    private static Dictionary<string,string> Dict(params (string Key,string Value)[] values)=>values.ToDictionary(x=>x.Key,x=>x.Value??"",StringComparer.Ordinal);
    private static Dictionary<string,string> Merge(IReadOnlyDictionary<string,string>a,IReadOnlyDictionary<string,string>b){var r=new Dictionary<string,string>(a,StringComparer.Ordinal);foreach(var x in b)r[x.Key]=x.Value??"";return r;}
    private static string Get(IReadOnlyDictionary<string,string>d,string k)=>d.TryGetValue(k,out var v)?v:"";
}
