using System;
using System.IO;
using System.Text.Json;
using VoltTray.Cli;
using VoltTray.Core.Models;
using VoltTray.Hosting;
using VoltTray.Infrastructure.Hid;
using VoltTray.Infrastructure.Protocol;
using VoltTray.Tests.ObjectMothers;
using Xunit;

namespace VoltTray.Tests;

public class ReadOnlyUpsProbeTests
{
    [Theory]
    [InlineData(VoltronicProtocolConstants.ProtocolIdentityCommand, UpsDiagnosticInquiry.ProtocolIdentity)]
    [InlineData(VoltronicProtocolConstants.QueryStatusCommand, UpsDiagnosticInquiry.QuickStatus)]
    [InlineData("QS", UpsDiagnosticInquiry.QuickStatus)]
    [InlineData(VoltronicProtocolConstants.MegatecStatusCommand, UpsDiagnosticInquiry.MegatecStatus)]
    [InlineData(VoltronicProtocolConstants.GeneralStatusCommand, UpsDiagnosticInquiry.GeneralStatus)]
    [InlineData(VoltronicProtocolConstants.NominalRatingsCommand, UpsDiagnosticInquiry.NominalRatings)]
    [InlineData(VoltronicProtocolConstants.VendorInformationCommand, UpsDiagnosticInquiry.VendorInformation)]
    public void VoltronicInquiryCodec_MapsReadOnlyWireCommandsToDomainEnum(
        string rawCommand,
        UpsDiagnosticInquiry expectedInquiry)
    {
        bool parsed = VoltronicInquiryCodec.TryParseWireCommand(rawCommand, out UpsDiagnosticInquiry parsedInquiry);

        Assert.True(parsed);
        Assert.Equal(expectedInquiry, parsedInquiry);
        Assert.Equal(
            rawCommand.EndsWith(VoltronicProtocolConstants.CarriageReturnChar)
                ? rawCommand
                : rawCommand + VoltronicProtocolConstants.CarriageReturnChar,
            VoltronicInquiryCodec.ToWireCommand(parsedInquiry));
    }

    [Theory]
    [InlineData(VoltronicProtocolConstants.BeeperToggleCommand)]
    [InlineData(VoltronicProtocolConstants.SelfTestCommand)]
    [InlineData(VoltronicProtocolConstants.CancelSelfTestCommand)]
    [InlineData(VoltronicProtocolConstants.LongSelfTestCommand)]
    [InlineData("S01R0001")]
    [InlineData(VoltronicProtocolConstants.CalibrationCommand)]
    [InlineData("PE\r")]
    [InlineData("")]
    public void VoltronicInquiryCodec_RejectsMutatingOrUnknownCommands(string rawCommand)
    {
        bool parsed = VoltronicInquiryCodec.TryParseWireCommand(rawCommand, out _);
        Assert.False(parsed);
    }

    [Fact]
    public void HidReportFramer_CreateOutputReports_ChunksCommandIntoNineByteReports()
    {
        IReadOnlyList<byte[]> reports = HidReportFramer.CreateOutputReports(VoltronicProtocolConstants.FeatureFlagsCommand);

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
        var descriptor = CreateTestDescriptor();

        UpsProbeCapture capture = ReadOnlyUpsProbe.BuildCapture(
            descriptor,
            UpsDiagnosticInquiry.QuickStatus,
            [[0x00, 0x23, 0x72, 0x00]],
            VoltronicFrameMother.Nt2401ProLiveFrame);

        Assert.Equal(UpsDiagnosticInquiry.QuickStatus, capture.Inquiry);
        Assert.Equal(VoltronicProtocolConstants.QueryStatusCommand, capture.WireCommand);
        Assert.Equal(11, capture.Tokens.Count);
        Assert.Equal(59.97, capture.FrequencyHz);
        Assert.NotNull(capture.StatusByte2Ratings);
        Assert.Equal(60.0, capture.StatusByte2Ratings.Value.NominalFrequencyHz);
        Assert.Equal(24.0, capture.StatusByte2Ratings.Value.NominalBatteryVoltageDc);
        Assert.Equal(120.0, capture.StatusByte2Ratings.Value.NominalOutputVoltageAc);
    }

    [Fact]
    public void UpsProbeCaptureFormatter_SerializeJson_ContainsDeviceRawHexTokensAndRatings()
    {
        var descriptor = CreateTestDescriptor();

        UpsProbeCapture capture = ReadOnlyUpsProbe.BuildCapture(
            descriptor,
            UpsDiagnosticInquiry.QuickStatus,
            [[0x00, 0x23, 0x72, 0x00]],
            VoltronicFrameMother.Nt2401ProLiveFrame);

        string json = UpsProbeCaptureFormatter.SerializeJson(capture);
        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        Assert.Equal("NT-2401PRO", root.GetProperty("Device").GetProperty("Product").GetString());
        Assert.Equal("QuickStatus", root.GetProperty("Inquiry").GetString());
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
    public void InspectModeStrategy_BlockedOrMissingArguments_ReturnsOneWithoutThrowing(params string[] args)
    {
        ApplicationOptions options = ApplicationOptionsParser.Parse(args);
        using var output = new StringWriter();
        var strategy = new InspectModeStrategy(output);

        int exitCode = strategy.Execute(options);

        Assert.Equal(1, exitCode);
        Assert.False(string.IsNullOrWhiteSpace(output.ToString()));
    }

    private static HidDeviceDescriptor CreateTestDescriptor()
    {
        return new HidDeviceDescriptor(
            @"\\?\HID#VID_0665&PID_5161#test",
            HidConstants.VendorId,
            HidConstants.ProductId,
            0x0100,
            "Forza",
            "NT-2401PRO",
            "(none)",
            HidConstants.ReportLength,
            HidConstants.ReportLength);
    }
}
