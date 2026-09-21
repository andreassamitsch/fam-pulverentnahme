using System.Xml.Linq;
using Fam.Pulverentnahme.Web;
using Xunit;

namespace Fam.Pulverentnahme.Web.Tests;

public sealed class FaAbortStornoPayloadTests
{
    private static readonly FaFeedbackReference Feedback = new(
        "103", "33806", "11.59.48", "FA24FK00126", "2026-09-18",
        10, "MT", "RP.00010", 20.160m, "EOS1", "RP00010MIX_20260909_140218");

    [Fact]
    public void StornoRequestContainsOnlyCapturedGetHdrFieldsAndExactFeedbackReference()
    {
        // The captured PW22021R *GETHDR result contains 26 fields. The captured *STORNO
        // request contains those fields plus only four new report-key fields.
        var header = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AKAUME"]="2,000", ["MEKOB2"]="PCE", ["I_AKIDNR"]="VK.06250",
            ["IDNRBZ"]="Cover Casing", ["AKWGAM"]="0", ["AKASTA"]="8",
            ["MEKAB2"]="PCE", ["FAUNBZ"]="", ["MEK1B5"]="Stück",
            ["MEK1B4"]="Stück", ["MEK1B3"]="Stück", ["ASTABZ"]="Teilbeendet",
            ["MEK1B2"]="Stück", ["MEKWB2"]="PCE",
            ["ARFAUN"]="FA24FK00126", ["_TITLE_"]="", ["TX_MEK19"]="Stk",
            ["DETAILPGM"]="", ["SSID"]="trace-session", ["MEK1BZ"]="Stück",
            ["MEKGB2"]="PCE", ["AKMEK1"]="PCE", ["AKIDNR"]="VK.06250",
            ["AKGUME"]="2,000", ["AKASME"]="0", ["OOFMG"]="0"
        };
        var payload = FaAbortCorrectionService.BuildStornoPayload(header, "trace-session", Feedback);
        Assert.Equal(30, payload.Count);
        Assert.Equal("33806", payload["ARRMNR"]);
        Assert.Equal("11.59.48", payload["ARRMZT"]);
        Assert.Equal("2026-09-18", payload["ARYRML"]);
        Assert.Equal("103", payload["ARFIRM"]);
        Assert.Equal("FA24FK00126", payload["ARFAUN"]);
        Assert.Equal("trace-session", payload["SSID"]);
        Assert.False(payload.ContainsKey("STORNO"));
        Assert.False(payload.ContainsKey("ARSTOR"));
        Assert.False(payload.ContainsKey("ARPENU"));
        Assert.False(payload.ContainsKey("STTOBI"));
        Assert.False(payload.ContainsKey("ARPOSN"));
    }

    [Fact]
    public void MismatchingGetHdrOrderDoesNotAllowStorno()
    {
        var header = new Dictionary<string, string> { ["ARFAUN"] = "DIFFERENT", ["SSID"] = "trace-session" };
        Assert.Throws<ProcessConflictException>(() =>
            FaAbortCorrectionService.BuildStornoPayload(header, "trace-session", Feedback));
    }

    [Fact]
    public void StandardStornoListWithOneRowWithoutArstorIsAccepted()
    {
        var row = Row("33806", null);
        var xml = XDocument.Parse($"<PARM><TABLE>{row}<STOP/></TABLE></PARM>");
        var selected = FaAbortCorrectionService.FindUniqueFeedback(
            xml, Feedback.OrderNo, Feedback.MaterialPosition, Feedback.Article,
            Feedback.QuantityKg, Feedback.Warehouse, Feedback.Batch);
        Assert.Equal(Feedback.Key, selected.Key);
    }

    [Fact]
    public void ExplicitlyCancelledRowIsExcludedWhileValidOneCanBeSelected()
    {
        var xml = XDocument.Parse($"<PARM><TABLE>{Row("33805", "J")}{Row("33806", "N")}<STOP/></TABLE></PARM>");
        var selected = FaAbortCorrectionService.FindUniqueFeedback(
            xml, Feedback.OrderNo, Feedback.MaterialPosition, Feedback.Article,
            Feedback.QuantityKg, Feedback.Warehouse, Feedback.Batch);
        Assert.Equal("33806", selected.ReportNo);
    }

    [Fact]
    public void TwoValidMatchingRowsRemainAnUnsafeAmbiguity()
    {
        var xml = XDocument.Parse($"<PARM><TABLE>{Row("33805", "N")}{Row("33806", "N")}<STOP/></TABLE></PARM>");
        Assert.Throws<ProcessConflictException>(() => FaAbortCorrectionService.FindUniqueFeedback(
            xml, Feedback.OrderNo, Feedback.MaterialPosition, Feedback.Article,
            Feedback.QuantityKg, Feedback.Warehouse, Feedback.Batch));
    }

    private static string Row(string reportNo, string? storno) => $"""
        <ROW><KEY><ARFIRM>103</ARFIRM><ARRMNR>{reportNo}</ARRMNR><ARRMZT>11.59.48</ARRMZT><ARFAUN>FA24FK00126</ARFAUN><ARYRML>2026-09-18</ARYRML></KEY>
        <PWARMP.ARAKKZ>MT</PWARMP.ARAKKZ><PWARMP.ARPOSN>10</PWARMP.ARPOSN>
        <_INTERN.WW_TX50>RP.00010 20.16 kg</_INTERN.WW_TX50>
        <_INTERN.WW_TX70B>RP.00010 EOS1  RP00010MIX_20260909_140218</_INTERN.WW_TX70B>
        {(storno is null ? "" : $"<ARSTOR>{storno}</ARSTOR>")}
        </ROW>
        """;
}
