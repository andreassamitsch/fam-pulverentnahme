using System.Xml.Linq;
using Fam.Pulverentnahme.Web;
using Xunit;

namespace Fam.Pulverentnahme.Web.Tests;

public sealed class SeparateProcessFeaturesTests
{
    [Fact]
    public void FaMaterialListFindsExactPowderPosition()
    {
        var xml = XDocument.Parse("""
<ROOT><ROW><KEY><AMFAUN>FA25FK00001</AMFAUN><AMPOSN>10</AMPOSN><AMIDNK>RP.00010</AMIDNK></KEY><AMIDNK_TLST.TLBEZG>AlSi10Mg</AMIDNK_TLST.TLBEZG></ROW><STOP/></ROOT>
""");
        var rows = FaMaterialService.ParseMaterialList(xml, "FA25FK00001", "RP.00010");
        var row = Assert.Single(rows);
        Assert.Equal(10, row.Position);
        Assert.Equal("RP.00010", row.Article);
        Assert.Equal("AlSi10Mg", row.ArticleText);
    }

    [Fact]
    public void FaMaterialReadUsesRealConsumedFieldAndStatus()
    {
        var xml = XDocument.Parse("""
<ROOT><DTA><AMFAUN>FA25FK00001</AMFAUN><AMPOSN>10</AMPOSN><AMIDNK>RP.00010</AMIDNK><TX_IDNK02>AlSi10Mg</TX_IDNK02><AMMATB>15,410</AMMATB><AMMATV>15,420</AMMATV><AMMEKZ>KGM</AMMEKZ><AMMPST>9</AMMPST><TX_MPST>Komplett abgebucht</TX_MPST></DTA></ROOT>
""");
        var result = FaMaterialService.ParseMaterialRead(xml, "FA25FK00001", 10, "RP.00010", "");
        Assert.Equal(15.410m, result.RequiredKg);
        Assert.Equal(15.420m, result.ConsumedKg);
        Assert.Equal(9, result.MaterialStatus);
        Assert.False(result.MkBookingAllowed);
        Assert.Equal("Komplett abgebucht", result.MaterialStatusText);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(8, true)]
    [InlineData(9, false)]
    public void MkStatusGateMatchesConfirmedOxaionRule(int status, bool allowed) =>
        Assert.Equal(allowed, FaMaterialService.MkStatusAllowed(status));

    [Fact]
    public void EnteredActualConsumptionIsTheTargetAndIsNotAddedAgain() =>
        Assert.Equal(15.430m, FaMaterialService.TargetConsumed(15.420m, 15.430m));

    [Theory]
    [InlineData(10, 15.420, 9, 10, 15.420, true)]
    [InlineData(10, 15.419, 9, 10, 15.420, false)]
    [InlineData(10, 15.420, 0, 10, 15.420, false)]
    [InlineData(20, 15.420, 9, 10, 15.420, false)]
    public void FaConsumptionSuccessRequiresExactPositionQuantityAndStatus(
        int actualPosition, double actualConsumed, int actualStatus,
        int expectedPosition, double expectedConsumed, bool expected)
    {
        var row = new FaMaterialPositionResult(
            "FA25FK00001", actualPosition, "RP.00010", "AlSi10Mg", 15.410m,
            (decimal)actualConsumed, "KGM", actualStatus,
            actualStatus == 9 ? "Komplett abgebucht" : "Eingeplant / Reserviert",
            FaMaterialService.MkStatusAllowed(actualStatus), DateTimeOffset.UtcNow);

        Assert.Equal(expected, FaConsumptionService.IsExactMkResult(row, expectedPosition, (decimal)expectedConsumed));
    }

    [Fact]
    public void LabelReprintCandidateCountsConfirmedLabelsAndBlocksUnclearPrint()
    {
        var tankRequest = new TankOutRequest(
            "tank-1", "446", "Andreas Samitsch", "EOS1", "EOS 1 -Tank",
            "RP.00010", "AlSi10Mg", "MIX1", 127m, "FAMLAB", "KA1", 127m);
        var tank = new SeparateOperation
        {
            Kind = TankOutService.Kind,
            ClientOperationId = "tank-1",
            Status = TransactionStatuses.Success,
            DocumentNo = "FA26MB00101",
            RequestJson = SeparateOperationStore.SerializeRequest(tankRequest),
            UpdatedAt = new DateTimeOffset(2026, 9, 29, 16, 15, 0, TimeSpan.Zero)
        };

        SeparateOperation Print(string id, string status, int count, int minute) => new()
        {
            Kind = TankOutLabelPrintService.Kind,
            ClientOperationId = id,
            Status = status,
            RelatedOperationId = "tank-1",
            RequestJson = SeparateOperationStore.SerializeRequest(
                new TankOutLabelPrintRequest(id, "446", "Andreas Samitsch", "tank-1", count)),
            UpdatedAt = new DateTimeOffset(2026, 9, 29, 16, minute, 0, TimeSpan.Zero)
        };

        var candidate = TankOutLabelPrintService.BuildReprintCandidate(tank,
        [
            Print("p1", TransactionStatuses.Success, 6, 16),
            Print("p2", TransactionStatuses.Success, 2, 17),
            Print("p3", TransactionStatuses.Rejected, 99, 18),
            Print("p4", TransactionStatuses.Uncertain, 3, 19)
        ]);

        Assert.Equal(8, candidate.SuccessfulLabelsRequested);
        Assert.True(candidate.ReprintBlocked);
        Assert.Equal(TransactionStatuses.Uncertain, candidate.LastPrintStatus);
        Assert.Contains("p4", candidate.ReprintBlockReason);
    }

    [Theory]
    [InlineData("FA26MB00101", true)]
    [InlineData("rp.00010", true)]
    [InlineData("mix1", true)]
    [InlineData("KA1", true)]
    [InlineData("something-else", false)]
    public void LabelReprintCandidateSearchesDocumentArticleBatchAndDestination(string query, bool expected)
    {
        var candidate = new TankOutLabelReprintCandidate(
            "tank-1", "FA26MB00101", DateTimeOffset.UtcNow, "RP.00010", "AlSi10Mg",
            "MIX1", 127m, "FAMLAB", "KA1", 6, TransactionStatuses.Success,
            DateTimeOffset.UtcNow, false, "");

        Assert.Equal(expected, TankOutLabelPrintService.MatchesQuery(candidate, query));
    }

    [Theory]
    [InlineData("UNCERTAIN", true)]
    [InlineData("MANUAL_REVIEW_REQUIRED", true)]
    [InlineData("SUCCESS", false)]
    [InlineData("REJECTED", false)]
    [InlineData("CONFLICT", false)]
    public void LabelReprintBlocksOnlyUnclearPriorPrints(string status, bool expected) =>
        Assert.Equal(expected, TankOutLabelPrintService.IsUnclearPrintStatus(status));

    [Fact]
    public void TankOutLabelPrintTargetsExactVerifiedLeMovement()
    {
        MovementRow[] rows =
        [
            new("1", "LF", "RP.00010", "MIX1", "EOS1", "", 127m, "2026-09-29-16.15.40.250000"),
            new("1", "LE", "RP.00010", "MIX1", "FAMLAB", "KA1", 127m, "2026-09-29-16.15.40.289000")
        ];

        var row = TankOutLabelPrintService.FindUniqueLabelMovement(
            rows, "RP.00010", "MIX1", "FAMLAB", "KA1", 127m);

        Assert.Equal("LE", row.BookingKey);
        Assert.Equal("2026-09-29-16.15.40.289000", row.Timestamp);
    }

    [Fact]
    public void TankOutLabelPrintRejectsAmbiguousTargetMovement()
    {
        MovementRow[] rows =
        [
            new("1", "LE", "RP.00010", "MIX1", "FAMLAB", "KA1", 127m, "t1"),
            new("1", "LE", "RP.00010", "MIX1", "FAMLAB", "KA1", 127m, "t2")
        ];

        Assert.Throws<ProcessConflictException>(() =>
            TankOutLabelPrintService.FindUniqueLabelMovement(
                rows, "RP.00010", "MIX1", "FAMLAB", "KA1", 127m));
    }

    [Fact]
    public void WarehouseLabelPrintConfigUsesRecordedEk99102FormAndDynamicQueue()
    {
        var xml = XDocument.Parse("""
<PARM><TABLE>
<ROW SELECTED="TRUE">
  <KEY>
    <PRINTPGM>EK99102J</PRINTPGM><PG>EK99102J</PG><ARBR>N</ARBR><TMPT></TMPT>
    <DBLA>J</DBLA><OFLW>0</OFLW><PRPT>DEFAULT</PRPT><UARC>N</UARC>
    <PRTF>*EK99102P</PRTF><CTYC>*USER</CTYC>
  </KEY>
  <PRT>J</PRT><PRTF>*EK99102P</PRTF><BEZC>Etikett Wareneingang</BEZC>
  <FORA></FORA><UGOUTQ>TESTQUEUE</UGOUTQ><UGANKO>1</UGANKO><UGPASO></UGPASO>
  <UGHOLD>J</UGHOLD><UGSAVE>J</UGSAVE><IUARC>N</IUARC><UGCTYC>*USER</UGCTYC>
</ROW><STOP/>
</TABLE></PARM>
""");

        var row = TankOutLabelPrintService.FindUniqueWarehouseLabelPrintConfig(xml);
        var payload = TankOutLabelPrintService.BuildPrintTablePayload(row);

        Assert.Equal("*EK99102P", payload["PRTF"]);
        Assert.Equal("EK99102J", payload["PRINTPGM"]);
        Assert.Equal("TESTQUEUE", payload["UGOUTQ"]);
        Assert.Equal("1", payload["UGANKO"]);
        Assert.Equal("J", payload["UGHOLD"]);
    }

    [Fact]
    public void TankOutVerificationRequiresExactLfLePair()
    {
        var spec = new TransferSpec(1, "LF", "RP.00010", "AlSi10Mg", "EOS1", "EOS 1 -Tank", "", "MIX1",
            "FAMLAB", "FAM Labor", "RE1F1", "", 149.574m);
        MovementRow[] rows =
        [
            new("1", "LF", "RP.00010", "MIX1", "EOS1", "", 149.574m, "t1"),
            new("1", "LE", "RP.00010", "MIX1", "FAMLAB", "RE1F1", 149.574m, "t1")
        ];
        Assert.True(MaterialTransferBookingService.MovementsComplete([spec], rows, out _));
    }

    [Fact]
    public void LmLnVerificationCreatesNewTargetMix()
    {
        var spec = new TransferSpec(2, "LM", "RP.00010", "AlSi10Mg", "EOS1", "EOS 1 -Tank", "", "RP00010MIX_OLD",
            "EOS1", "EOS 1 -Tank", "", "RP00010MIX_NEW", 20m, new DateOnly(2026, 9, 8));
        MovementRow[] rows =
        [
            new("2", "LM", "RP.00010", "RP00010MIX_OLD", "EOS1", "", 20m, "t1"),
            new("2", "LN", "RP.00010", "RP00010MIX_NEW", "EOS1", "", 20m, "t1")
        ];
        Assert.True(MaterialTransferBookingService.MovementsComplete([spec], rows, out _));
    }
}
