using System.Xml.Linq;
using Fam.Pulverentnahme.Web;
using Xunit;

namespace Fam.Pulverentnahme.Web.Tests;

public sealed class ChargeOriginServiceTests
{
    [Fact]
    public void EntryPayloadReplaysCapturedUpostContextWithoutInventingObjectId()
    {
        var withoutObjectId = ChargeOriginService.BuildEntryDta(
            "RP.00010",
            "RP00010MIX_20261006_144459",
            null);

        Assert.Equal("RP00010MIX_20261006_144459", withoutObjectId["SEPONR"]);
        Assert.Equal("RP00010MIX_20261006_144459", withoutObjectId["QHPONR"]);
        Assert.Equal("RP00010MIX_20261006_144459", withoutObjectId["POPONR"]);
        Assert.Equal("RP.00010", withoutObjectId["SEIDNR"]);
        Assert.Equal("RP.00010", withoutObjectId["QHIDNR"]);
        Assert.Equal("RP.00010", withoutObjectId["POIDNR"]);
        Assert.Equal("UPOST", withoutObjectId["KEYTYPE"]);
        Assert.Equal("0", withoutObjectId["FIPOBID"]);
        Assert.False(withoutObjectId.ContainsKey("POOBID"));
        Assert.False(withoutObjectId.ContainsKey("FIOBID"));

        var withObjectId = ChargeOriginService.BuildEntryDta(
            "RP.00010",
            "RP00010MIX_20261006_144459",
            14089546);

        Assert.Equal("14089546", withObjectId["POOBID"]);
        Assert.Equal("14089546", withObjectId["FIOBID"]);
    }

    [Fact]
    public void ParsesCapturedTreeShapeAndNormalizesCompositeArticleColumn()
    {
        var xml = XDocument.Parse("""
            <PARM><TABLE>
              <ROW>
                <KEY><PEMPOS>00001.00001</PEMPOS><PESSID>ANSA1</PESSID></KEY>
                <UPOVEP.PESTCK>2</UPOVEP.PESTCK>
                <UPOVEP.PEPONR>84671</UPOVEP.PEPONR>
                <UPOVEP.PEIDNR>RP.00010              RP.00010</UPOVEP.PEIDNR>
                <UPOVEP.PELINR>3001399 000</UPOVEP.PELINR>
                <UPOVEP.PEBENR>FA24BE00022</UPOVEP.PEBENR>
                <UPOVEP.PEWEGN>FA24WE00027</UPOVEP.PEWEGN>
              </ROW>
              <ROW SUBTREES="TRUE">
                <KEY><PEMPOS>00001.00026</PEMPOS><PESSID>ANSA1</PESSID></KEY>
                <UPOVEP.PESTCK>2</UPOVEP.PESTCK>
                <UPOVEP.PEPONR>RP00010MIX_20261001_161906</UPOVEP.PEPONR>
                <UPOVEP.PEIDNR>RP.00010              RP.00010</UPOVEP.PEIDNR>
              </ROW>
              <STOP />
            </TABLE></PARM>
            """);

        var rows = ChargeOriginService.ParseRows(xml);

        Assert.Equal(2, rows.Count);
        Assert.Equal("RP.00010", rows[0].Article);
        Assert.Equal("84671", rows[0].Batch);
        Assert.Equal("3001399 000", rows[0].Supplier);
        Assert.False(rows[0].HasSubtrees);

        Assert.True(rows[1].HasSubtrees);
        Assert.Equal("ANSA1", rows[1].SessionKey);
        Assert.Equal("00001.00026", rows[1].PositionKey);
    }

    [Fact]
    public void BaseBatchReductionExcludesMixNodesEvenWhenSameBatchAlsoHasLeafRows()
    {
        var rows = new[]
        {
            // Captured root shape: same MIX charge has FA rows without SUBTREES and
            // one structural row with SUBTREES. It must not become a base batch.
            Row(1, "RP.00010", "RP00010MIX_20261006_144459", false, productionOrder: "FA26FK00015"),
            Row(1, "RP.00010", "RP00010MIX_20261006_144459", false, productionOrder: "FA26FK00017"),
            Row(1, "RP.00010", "RP00010MIX_20261006_144459", true),

            // Captured source shape: charge 84671 appears repeatedly in following FAs,
            // but the procurement row contains the useful origin metadata.
            Row(2, "RP.00010", "84671", false,
                supplier: "3001399 000",
                purchaseOrder: "FA24BE00022",
                goodsReceipt: "FA24WE00027"),
            Row(2, "RP.00010", "84671", false, productionOrder: "FA24FI00088"),
            Row(2, "RP.00010", "84671", false, productionOrder: "FA24FI00094"),

            // Another intermediate mix must also be removed.
            Row(2, "RP.00010", "RP00010MIX_20261001_161906", true),
            Row(3, "RP.00010", "RP00010MIX_20261001_161906", false, productionOrder: "FA26FK00001")
        };

        var result = ChargeOriginService.ReduceBaseBatches(rows);

        var baseBatch = Assert.Single(result);
        Assert.Equal("RP.00010", baseBatch.Article);
        Assert.Equal("84671", baseBatch.Batch);
        Assert.Equal("3001399 000", baseBatch.Supplier);
        Assert.Equal("FA24BE00022", baseBatch.PurchaseOrder);
        Assert.Equal("FA24WE00027", baseBatch.GoodsReceipt);
    }

    [Fact]
    public void ExpansionPayloadMatchesCapturedFirstAndDeeperTreeCalls()
    {
        var context = new Dictionary<string, string>
        {
            ["TX_USAGE"] = "CH",
            ["POOBID"] = "14089540",
            ["FIOBID"] = "14089540",
            ["PGMN"] = "US17476R",
            ["mode"] = "reset"
        };

        var first = ChargeOriginService.BuildExpansionDta(
            "179144406858836793",
            Row(1, "RP.00010", "ROOT", true, sessionKey: "ANSA1", positionKey: "00001"),
            context);

        Assert.Equal("CH", first["TX_USAGE"]);
        Assert.Equal("14089540", first["POOBID"]);
        Assert.Equal("ANSA1", first["PESSID"]);
        Assert.Equal("00001", first["PEMPOS"]);
        Assert.Equal("true", first["NoHeader"]);
        Assert.False(first.ContainsKey("mode"));

        var deeper = ChargeOriginService.BuildExpansionDta(
            "179144406858836793",
            Row(2, "RP.00010", "MIX", true, sessionKey: "ANSA1", positionKey: "00001.00026"),
            context);

        Assert.Equal("179144406858836793", deeper["SSID"]);
        Assert.Equal("ANSA1", deeper["PESSID"]);
        Assert.Equal("00001.00026", deeper["PEMPOS"]);
        Assert.Equal("true", deeper["NoHeader"]);
        Assert.False(deeper.ContainsKey("POOBID"));
        Assert.False(deeper.ContainsKey("TX_USAGE"));
    }

    [Fact]
    public void RejectsListWithoutCapturedStopCompletenessMarker()
    {
        var xml = XDocument.Parse("<PARM><TABLE><ROW /></TABLE></PARM>");
        Assert.Throws<ChargeOriginProtocolException>(
            () => ChargeOriginService.EnsureCompleteList(xml, "test"));
    }

    private static ChargeOriginRow Row(
        int level,
        string article,
        string batch,
        bool hasSubtrees,
        string supplier = "",
        string purchaseOrder = "",
        string deliveryNote = "",
        string productionOrder = "",
        string goodsReceipt = "",
        string sessionKey = "ANSA",
        string positionKey = "00001") =>
        new(
            level,
            article,
            batch,
            supplier,
            purchaseOrder,
            deliveryNote,
            productionOrder,
            goodsReceipt,
            sessionKey,
            positionKey,
            hasSubtrees);
}
