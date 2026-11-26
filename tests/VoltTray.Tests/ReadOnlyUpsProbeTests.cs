using System;
using System.IO;
using System.Text.Json;
using VoltTray.Cli;
using VoltTray.Infrastructure.Hid;
using VoltTray.Infrastructure.Protocol;
using VoltTray.Tests.ObjectMothers;
using Xunit;

namespace VoltTray.Tests;

public class ReadOnlyUpsProbeTests
{
    [Theory]
    [InlineData("M\r")]
    [InlineData("QS\r")]
    [InlineData("Q1\r")]
    [InlineData("QGS\r")]
    [InlineData("F\r")]
    [InlineData("I\r")]
    public void UpsInquiryCommandPolicy_AcceptsReadOnlyInquiryCommands(string command)
    {
        Assert.True(UpsInquiryCommandPolicy.IsReadOnlyInquiry(command));
        Assert.Equal(command, UpsInquiryCommandPolicy.EnsureReadOnlyInquiry(command));
    }

    [Theory]
    [InlineData("Q\r")]
    [InlineData("T\r")]
    [InlineData("CT\r")]
    [InlineData("TL\r")]
    [InlineData("S01R0001")]
    [InlineData("C\r")]
    [InlineData("PE\r")]
    public void UpsInquiryCommandPolicy_RejectsMutatingCommandsBeforeUsbWrite(string command)
    {
        Assert.False(UpsInquiryCommandPolicy.IsReadOnlyInquiry(command));
        Assert.Throws<InvalidOperationException>(() => UpsInquiryCommandPolicy.EnsureReadOnlyInquiry(command));
    }

    [Fact]
    public void HidReportFramer_CreateOutputReports_ChunksCommandIntoNineByteReports()
    {
        IReadOnlyList<byte[]> reports = HidReportFramer.CreateOutputReports("QFLAG\r");

        Assert.Single(reports);
        Assert.Equal(HidConstants.ReportLength, reports[0].Length);
        Assert.Equal(HidConstants.DefaultReportId, reports[0][0]);
        Assert.Equal((byte)'Q', reports[0][1]);
        Assert.Equal((byte)'\r', reports[0][6]);
        Assert.Equal(0x00, reports[0][7]);
    }

    [Fact]
    public void HidReportFramer_AssembleInputPayload_RetainsInteriorZeroBytesUpToCarriageReturn()
    {
        byte[][] reports =
        [
            [0x00, 0x23, 0x72, 0x00, 0x20, 0x34, 0x20, 0x72, 0x00],
            [0x00, 0x20, 0x34, 0x20, 0x0C, 0x20, 0x50, 0x0B, 0x20],
            [0x00, 0x12, 0xC0, 0x00, 0x20, 0xE9, 0x20, 0x3C, 0x20],
            [0x00, 0x09, 0x20, 0xA1, 0x0D, 0x00, 0x00, 0x00, 0x00]
        ];

        byte[] assembled = HidReportFramer.AssembleInputPayload(reports, requireFramingHeader: true);

        Assert.Equal(VoltronicFrameMother.Nt2401ProLiveFrame, assembled);
    }

    [Fact]
    public void HidReportFramer_AssembleInputPayload_PreservesNakAndPartialFramesWithoutCr()
    {
        byte[] nakPayload = HidReportFramer.AssembleInputPayload([[0x00, 0x4E, 0x0D, 0x00]]);
        byte[] partialPayload = HidReportFramer.AssembleInputPayload([[0x00, 0x23, 0x72, 0x00]]);

        Assert.Equal([0x4E, 0x0D], nakPayload);
        Assert.Equal([0x23, 0x72, 0x00], partialPayload);
    }

    [Fact]
    public void ReadOnlyUpsProbe_BuildCapture_ExtractsTokensFrequencyAndStatusByte2()
    {
        var descriptor = new HidDeviceDescriptor(
            @"\\?\HID#VID_0665&PID_5161#test",
            0x0665,
            0x5161,
            0x0100,
            "Forza",
            "NT-2401PRO",
            "(none)",
            9,
            9);

        UpsProbeCapture capture = ReadOnlyUpsProbe.BuildCapture(
            descriptor,
            "QS\r",
            [[0x00, 0x23, 0x72, 0x00]],
            VoltronicFrameMother.Nt2401ProLiveFrame);

        Assert.Equal(11, capture.Tokens.Count);
        Assert.Equal(59.97, capture.FrequencyHz);
        Assert.NotNull(capture.StatusByte2Ratings);
        Assert.Equal(60.0, capture.StatusByte2Ratings.Value.NominalFrequencyHz);
        Assert.Equal(24.0, capture.StatusByte2Ratings.Value.NominalBatteryVoltageDc);
        Assert.Equal(120.0, capture.StatusByte2Ratings.Value.NominalOutputVoltageAc);
    }

    [Fact]
    public void InspectCommandRunner_SerializeCaptureJson_ContainsDeviceRawHexTokensAndRatings()
    {
        var descriptor = new HidDeviceDescriptor(
            @"\\?\HID#VID_0665&PID_5161#test",
            0x0665,
            0x5161,
            0x0100,
            "Forza",
            "NT-2401PRO",
            "(none)",
            9,
            9);

        UpsProbeCapture capture = ReadOnlyUpsProbe.BuildCapture(
            descriptor,
            "QS\r",
            [[0x00, 0x23, 0x72, 0x00]],
            VoltronicFrameMother.Nt2401ProLiveFrame);

        string json = InspectCommandRunner.SerializeCaptureJson(capture);
        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        Assert.Equal("NT-2401PRO", root.GetProperty("Device").GetProperty("Product").GetString());
        Assert.Contains("72 00", root.GetProperty("RawHex").GetString());
        Assert.Contains("12 C0 00", root.GetProperty("RawHex").GetString());
        Assert.True(root.GetProperty("TokenHexBreakdown").GetArrayLength() >= 11);

        JsonElement ratings = root.GetProperty("StatusByte2Ratings");
        Assert.Equal(60.0, ratings.GetProperty("NominalFrequencyHz").GetDouble());
        Assert.Equal(24.0, ratings.GetProperty("NominalBatteryVoltageDc").GetDouble());
        Assert.Equal(120.0, ratings.GetProperty("NominalOutputVoltageAc").GetDouble());
    }

    [Theory]
    [InlineData("--inspect", "--cmd", "Q")]
    [InlineData("--inspect", "--cmd")]
    [InlineData("--inspect", "--export")]
    public void InspectCommandRunner_Run_BlockedOrMissingArguments_ReturnsOneWithoutThrowing(params string[] args)
    {
        using var output = new StringWriter();
        int exitCode = InspectCommandRunner.Run(args, output);

        Assert.Equal(1, exitCode);
        Assert.False(string.IsNullOrWhiteSpace(output.ToString()));
    }
}
