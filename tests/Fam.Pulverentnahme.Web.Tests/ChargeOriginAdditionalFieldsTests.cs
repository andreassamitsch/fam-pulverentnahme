using System.Xml.Linq;
using Fam.Pulverentnahme.Web;
using Xunit;

namespace Fam.Pulverentnahme.Web.Tests;

/// <summary>
/// Regression examples taken from the actual 2026-10-08 Oxaion
/// US17476R *FIRSTLIST trace with additional configured columns.
/// </summary>
public sealed class ChargeOriginAdditionalFieldsTests
{
    [Theory]
    [InlineData("84671", "FA24WE00027", "2024-05-06")]
    [InlineData("87911", "FA24WE00038", "2024-06-21")]
    public void ParsesSupplierNameAndDeliveryDateFromReceiptRow(
        string batch,
        string goodsReceipt,
        string deliveryDate)
    {
        var xml = XDocument.Parse($"""
            <PARM><TABLE><ROW>
              <KEY><PESSID>ANSA1</PESSID><PEMPOS>00001</PEMPOS></KEY>
              <UPOVEP.PESTCK>2</UPOVEP.PESTCK>
              <UPOVEP.PEIDNR>RP.00010              RP.00010</UPOVEP.PEIDNR>
              <UPOVEP.PEPONR>{batch}</UPOVEP.PEPONR>
              <UPOVEP.PELINR>3001399 000</UPOVEP.PELINR>
              <T_TEXT_PELINR_UPOVEP.T_TEXT_PELINR_UPOVEP_TX_PKOAZL1>IMR metal powder technologies GmbH</T_TEXT_PELINR_UPOVEP.T_TEXT_PELINR_UPOVEP_TX_PKOAZL1>
              <UPOVEP.PEBENR>FA24BE00022</UPOVEP.PEBENR>
              <UPOVEP.PEWEGN>{goodsReceipt}</UPOVEP.PEWEGN>
              <UPOVEP.PELFDT>{deliveryDate}</UPOVEP.PELFDT>
            </ROW><STOP/></TABLE></PARM>
            """);

        var row = Assert.Single(ChargeOriginService.ParseRows(xml));
        Assert.Equal("IMR metal powder technologies GmbH", row.SupplierName);
        Assert.Equal(deliveryDate, row.DeliveryDate);
        Assert.Equal("", row.ExternalBatch);

        var batchResult = Assert.Single(ChargeOriginService.ReduceBaseBatches([row]));
        Assert.Equal(batch, batchResult.Batch);
        Assert.Equal("3001399 000", batchResult.Supplier);
        Assert.Equal("IMR metal powder technologies GmbH", batchResult.SupplierName);
        Assert.Equal(goodsReceipt, batchResult.GoodsReceipt);
        Assert.Equal(deliveryDate, batchResult.DeliveryDate);
    }

    [Fact]
    public void ExternalLotIsTakenFromBatchRowsEvenWithoutGoodsReceipt()
    {
        // For batch 52993, the actual trace puts PONR.POCHNL on
        // FA-consumption rows, without WE or supplier metadata.
        var xml = XDocument.Parse("""
            <PARM><TABLE>
              <ROW>
                <KEY><PESSID>ANSA1</PESSID><PEMPOS>00001</PEMPOS></KEY>
                <UPOVEP.PESTCK>39</UPOVEP.PESTCK>
                <UPOVEP.PEIDNR>RP.00010              RP.00010</UPOVEP.PEIDNR>
                <UPOVEP.PEPONR>52993</UPOVEP.PEPONR>
                <PONR.POCHNL>WZ_17551102+WZ_1761113_m4p_BS2</PONR.POCHNL>
                <UPOVEP.PEFAUN>FA23FI00006</UPOVEP.PEFAUN>
              </ROW>
              <ROW>
                <KEY><PESSID>ANSA1</PESSID><PEMPOS>00002</PEMPOS></KEY>
                <UPOVEP.PESTCK>39</UPOVEP.PESTCK>
                <UPOVEP.PEIDNR>RP.00010              RP.00010</UPOVEP.PEIDNR>
                <UPOVEP.PEPONR>52993</UPOVEP.PEPONR>
                <PONR.POCHNL>WZ_17551102+WZ_1761113_m4p_BS2</PONR.POCHNL>
                <UPOVEP.PEFAUN>FA24FI00001</UPOVEP.PEFAUN>
              </ROW><STOP/>
            </TABLE></PARM>
            """);

        var result = Assert.Single(ChargeOriginService.ReduceBaseBatches(
            ChargeOriginService.ParseRows(xml)));

        Assert.Equal("52993", result.Batch);
        Assert.Equal("WZ_17551102+WZ_1761113_m4p_BS2", result.ExternalBatch);
        Assert.Equal("", result.SupplierName);
        Assert.Equal("", result.DeliveryDate);
        Assert.Equal("", result.GoodsReceipt);
    }

    [Fact]
    public void DoesNotInventSupplementalValuesWhenMissingOrConflicting()
    {
        var missing = new ChargeOriginRow(1, "RP.00010", "EMPTY", "", "", "", "", "",
            "S", "1", false);
        var duplicate = new ChargeOriginRow(1, "RP.00010", "LOT", "", "", "",
            "FA23FI00001", "", "S", "2", false,
            ExternalBatch: "EXT-A", DeliveryDate: "2026-01-12");
        var conflicting = duplicate with { ExternalBatch = "EXT-B", PositionKey = "3" };

        var origin = ChargeOriginService.ReduceBaseBatches([missing, duplicate, conflicting]);
        var empty = Assert.Single(origin.Where(x => x.Batch == "EMPTY"));
        var lot = Assert.Single(origin.Where(x => x.Batch == "LOT"));

        Assert.Equal("", empty.ExternalBatch);
        Assert.Equal("", empty.DeliveryDate);
        Assert.Equal("", lot.ExternalBatch);
        Assert.Equal("", lot.DeliveryDate); // Cannot infer a receipt date from an FA-only row.
    }

    [Fact]
    public void AmbiguousReceiptsCannotSelectSupplierNameOrDeliveryDate()
    {
        var first = new ChargeOriginRow(2, "RP.00010", "MULTI", "100 000",
            "BE1", "", "", "WE1", "S", "1", false,
            SupplierName: "First supplier", DeliveryDate: "2024-01-01");
        var second = first with
        {
            GoodsReceipt = "WE2", PositionKey = "2",
            SupplierName = "Second supplier", DeliveryDate = "2024-02-01"
        };
        var item = Assert.Single(ChargeOriginService.ReduceBaseBatches([first, second]));

        Assert.Equal("", item.SupplierName);
        Assert.Equal("", item.DeliveryDate);
    }
}
