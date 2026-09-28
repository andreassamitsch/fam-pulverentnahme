using System.Xml.Linq;
using Fam.Pulverentnahme.Web;
using Xunit;

namespace Fam.Pulverentnahme.Web.Tests;

public sealed class JobAbortAndTankWeighingTests
{
    [Theory]
    [InlineData("I2", 0.001)]
    [InlineData("I1", 0.002)]
    public void InventoryCorrectionRequiresExactlyOneConfirmedMovement(string key, double qty)
    {
        MovementRow[] rows =
        [
            new("1", key, "RP.00010", "RP00010MIX_20260909_140218", "EOS1", "", (decimal)qty, "2026-09-18-12.19.07")
        ];

        Assert.True(MaterialTransferBookingService.CorrectionMovementComplete(
            rows, key, "RP.00010", "EOS1", "RP00010MIX_20260909_140218", (decimal)qty, out _));
    }

    [Fact]
    public void InventoryCorrectionRejectsUnexpectedAdditionalMovement()
    {
        MovementRow[] rows =
        [
            new("1", "I2", "RP.00010", "MIX1", "EOS1", "", 0.001m, "t1"),
            new("1", "I2", "RP.00010", "MIX1", "EOS1", "", 0.001m, "t2")
        ];

        Assert.False(MaterialTransferBookingService.CorrectionMovementComplete(
            rows, "I2", "RP.00010", "EOS1", "MIX1", 0.001m, out _));
    }

    [Fact]
    public void StornoCandidateIsSelectedByExactFaPositionQuantityTankAndMix()
    {
        var xml = XDocument.Parse("""
<PARM><TABLE>
<ROW>
  <KEY><ARFIRM>103</ARFIRM><ARRMNR>33806</ARRMNR><ARRMZT>11.59.48</ARRMZT><ARFAUN>FA24FK00126</ARFAUN><ARYRML>2026-09-18</ARYRML></KEY>
  <PWARMP.ARAKKZ>MT</PWARMP.ARAKKZ>
  <PWARMP.ARPOSN>10</PWARMP.ARPOSN>
  <_INTERN.WW_TX50>RP.00010 20.16 kg</_INTERN.WW_TX50>
  <_INTERN.WW_TX70B>RP.00010 EOS1  RP00010MIX_20260909_140218</_INTERN.WW_TX70B>
</ROW>
<STOP/>
</TABLE></PARM>
""");

        var row = FaAbortCorrectionService.FindUniqueFeedback(
            xml, "FA24FK00126", 10, "RP.00010", 20.160m, "EOS1", "RP00010MIX_20260909_140218");

        Assert.Equal("33806", row.ReportNo);
        Assert.Equal("11.59.48", row.ReportTime);
        Assert.Equal("2026-09-18", row.ReportDate);
        Assert.Equal("MT", row.Transaction);
    }

    [Fact]
    public void SingleValidOxaionStornoRowIsAcceptedEvenIfSourceDisplayTextDiffers()
    {
        var xml = XDocument.Parse("""
<PARM><TABLE>
<ROW>
  <KEY><ARFIRM>103</ARFIRM><ARRMNR>44001</ARRMNR><ARRMZT>15.02.06</ARRMZT><ARFAUN>FA24FI00118</ARFAUN><ARYRML>2026-09-08</ARYRML></KEY>
  <PWARMP.ARAKKZ>MK</PWARMP.ARAKKZ>
  <PWARMP.ARPOSN>10</PWARMP.ARPOSN>
  <_INTERN.WW_TX50>RP.00010 2.815 kg</_INTERN.WW_TX50>
  <_INTERN.WW_TX70B>RP.00010 EOS1 DISPLAYED_SOURCE_TEXT</_INTERN.WW_TX70B>
</ROW>
<STOP/>
</TABLE></PARM>
""");

        var row = FaAbortCorrectionService.FindUniqueFeedback(
            xml,
            "FA24FI00118",
            10,
            "RP.00010",
            2.815m,
            "EOS1",
            "RP00010MIX_20260909_140218");

        Assert.Equal("44001", row.ReportNo);
        Assert.Equal("FA24FI00118", row.OrderNo);
        Assert.Equal(2.815m, row.QuantityKg);
    }

    [Fact]
    public void StornoCandidateFailsClosedWhenMultipleCoreMatchesCannotBeResolvedByTankAndMix()
    {
        var xml = XDocument.Parse("""
<PARM><TABLE>
<ROW><KEY><ARFIRM>103</ARFIRM><ARRMNR>1</ARRMNR><ARRMZT>10.00.00</ARRMZT><ARFAUN>FA24FK00126</ARFAUN><ARYRML>2026-09-18</ARYRML></KEY><PWARMP.ARAKKZ>MT</PWARMP.ARAKKZ><PWARMP.ARPOSN>10</PWARMP.ARPOSN><_INTERN.WW_TX50>RP.00010 20.16 kg</_INTERN.WW_TX50><_INTERN.WW_TX70B>RP.00010 EOS1 MIX1</_INTERN.WW_TX70B></ROW>
<ROW><KEY><ARFIRM>103</ARFIRM><ARRMNR>2</ARRMNR><ARRMZT>10.01.00</ARRMZT><ARFAUN>FA24FK00126</ARFAUN><ARYRML>2026-09-18</ARYRML></KEY><PWARMP.ARAKKZ>MT</PWARMP.ARAKKZ><PWARMP.ARPOSN>10</PWARMP.ARPOSN><_INTERN.WW_TX50>RP.00010 20.16 kg</_INTERN.WW_TX50><_INTERN.WW_TX70B>RP.00010 EOS1 MIX1</_INTERN.WW_TX70B></ROW>
<STOP/>
</TABLE></PARM>
""");

        Assert.Throws<ProcessConflictException>(() =>
            FaAbortCorrectionService.FindUniqueFeedback(
                xml, "FA24FK00126", 10, "RP.00010", 20.160m, "EOS1", "OTHER_MIX"));
    }

    [Theory]
    [InlineData(32.3504, 32.350)]
    [InlineData(32.3505, 32.351)]
    public void TankWeightIsNormalizedToConfirmedThreeDecimalOxaionQuantity(double input, double expected) =>
        Assert.Equal((decimal)expected, TankOutService.RoundKg((decimal)input));
}
