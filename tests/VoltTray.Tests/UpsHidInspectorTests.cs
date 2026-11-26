using System;
using System.Text.Json;
using VoltTray.Infrastructure.Hid;
using VoltTray.Tests.ObjectMothers;
using Xunit;

namespace VoltTray.Tests;

public class UpsHidInspectorTests
{
    [Theory]
    [InlineData("M\r")] [InlineData("QS\r")] [InlineData("Q1\r")]
    [InlineData("QGS\r")] [InlineData("F\r")] [InlineData("I\r")]
    public void Allowlist_AcceptsReadOnlyInquiryCommands(string command)
    {
        Assert.True(UpsHidInspector.IsAllowedInquiryCommand(command));
        Assert.Equal(command, UpsHidInspector.EnsureAllowedInquiryCommand(command));
    }

    [Theory]
    [InlineData("Q\r")] [InlineData("T\r")] [InlineData("CT\r")]
    [InlineData("TL\r")] [InlineData("S01R0001")] [InlineData("C\r")] [InlineData("PE\r")]
    public void Allowlist_RejectsMutatingCommandsBeforeUsbWrite(string command)
    {
        Assert.False(UpsHidInspector.IsAllowedInquiryCommand(command));
        Assert.Throws<InvalidOperationException>(() => UpsHidInspector.EnsureAllowedInquiryCommand(command));
    }

    [Fact]
    public void AssemblePayloadPreservingNulls_RetainsInteriorZeroBytesUpToCarriageReturn()
    {
        byte[][] reports =
        [
            [0x00, 0x23, 0x72, 0x00, 0x20, 0x34, 0x20, 0x72, 0x00],
            [0x00, 0x20, 0x34, 0x20, 0x0C, 0x20, 0x50, 0x0B, 0x20],
            [0x00, 0x12, 0xC0, 0x00, 0x20, 0xE9, 0x20, 0x3C, 0x20],
            [0x00, 0x09, 0x20, 0xA1, 0x0D, 0x00, 0x00, 0x00, 0x00]
        ];
        Assert.Equal(VoltronicFrameMother.Nt2401ProLiveFrame, UpsHidInspector.AssemblePayloadPreservingNulls(reports, requireLeadingHeader: true));
    }

    [Fact]
    public void SerializeCaptureJson_ContainsDeviceMetadataRawHexWithNullsTokensAndRatings()
    {
        var descriptor = new HidDeviceDescriptor(@"\\?\HID#VID_0665&PID_5161#test", 0x0665, 0x5161, 0x0100, "Forza", "NT-2401PRO", "(none)", 9, 9);
        var report = UpsHidInspector.BuildReport(descriptor, "QS\r", [[0x00, 0x23, 0x72, 0x00]], VoltronicFrameMother.Nt2401ProLiveFrame);
        using var doc = JsonDocument.Parse(UpsHidInspector.SerializeCaptureJson(report));
        var root = doc.RootElement;
        Assert.Equal("NT-2401PRO", root.GetProperty("Device").GetProperty("Product").GetString());
        Assert.Contains("72 00", root.GetProperty("RawHex").GetString());
        Assert.Contains("12 C0 00", root.GetProperty("RawHex").GetString());
        Assert.True(root.GetProperty("TokenHexBreakdown").GetArrayLength() >= 11);
        var ratings = root.GetProperty("StatusByte2Ratings");
        Assert.Equal((60.0, 24.0, 120.0), (ratings.GetProperty("NominalFrequencyHz").GetDouble(), ratings.GetProperty("NominalBatteryVoltageDc").GetDouble(), ratings.GetProperty("NominalOutputVoltageAc").GetDouble()));
    }

    [Fact]
    public void AssemblePayloadPreservingNulls_PreservesNakAndPartialFramesWithoutCr()
    {
        Assert.Equal(new byte[] { 0x4E, 0x0D }, UpsHidInspector.AssemblePayloadPreservingNulls([[0x00, 0x4E, 0x0D, 0x00]]));
        Assert.Equal(new byte[] { 0x23, 0x72, 0x00 }, UpsHidInspector.AssemblePayloadPreservingNulls([[0x00, 0x23, 0x72, 0x00]]));
    }

    [Theory]
    [InlineData("--inspect", "--cmd", "Q")]
    [InlineData("--inspect", "--cmd")]
    public void RunCli_BlockedOrMissingCmd_ReturnsOneWithoutThrowing(params string[] args)
    {
        using var sw = new System.IO.StringWriter();
        Assert.Equal(1, UpsHidInspector.RunCli(args, sw));
        Assert.False(string.IsNullOrWhiteSpace(sw.ToString()));
    }
}
