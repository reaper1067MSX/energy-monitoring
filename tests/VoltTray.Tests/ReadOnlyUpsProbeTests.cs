using VoltTray.Core.Diagnostics;
using VoltTray.Infrastructure.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using VoltTray.Cli;
using VoltTray.Hosting;
using VoltTray.Infrastructure.Hid;
using VoltTray.Infrastructure.Protocol;
using VoltTray.Tests.ObjectMothers;
using Xunit;

namespace VoltTray.Tests;

public class ReadOnlyUpsProbeTests
{
    private const double ExpectedNt2401ProFrequencyHz = 59.97;
    private const double ExpectedNominalFrequencyHz = 60.0;
    private const double ExpectedNominalBatteryVoltageDc = 24.0;
    private const double ExpectedNominalOutputVoltageAc = 120.0;

    [Theory]
    [InlineData(VoltronicProtocolConstants.ProtocolIdentityCommand, UpsDiagnosticInquiry.ProtocolIdentity)]
    [InlineData(VoltronicProtocolConstants.QueryStatusCommand, UpsDiagnosticInquiry.QuickStatus)]
    [InlineData("QS", UpsDiagnosticInquiry.QuickStatus)]
    [InlineData(VoltronicProtocolConstants.StandardAsciiStatusCommand, UpsDiagnosticInquiry.StandardAsciiStatus)]
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
    [InlineData(VoltronicFixtureConstants.CancelSelfTestCommand)]
    [InlineData(VoltronicFixtureConstants.LongSelfTestCommand)]
    [InlineData(VoltronicFixtureConstants.ShutdownRestoreTestCommand)]
    [InlineData(VoltronicFixtureConstants.CalibrationCommand)]
    [InlineData(VoltronicFixtureConstants.UnknownPeCommand)]
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
        Assert.Equal((byte)'Q', reports[0][HidConstants.ReportPayloadStartIndex]);
        Assert.Equal(VoltronicProtocolConstants.CarriageReturn, reports[0][VoltronicProtocolConstants.FeatureFlagsCommand.Length]);
        Assert.Equal(VoltronicProtocolConstants.NullByte, reports[0][VoltronicProtocolConstants.FeatureFlagsCommand.Length + 1]);
    }

    [Fact]
    public void HidReportFramer_AssembleInputPayload_RetainsInteriorZeroBytesUpToCarriageReturn()
    {
        byte[] liveFrame = VoltronicFrameMother.Nt2401ProLiveFrame;
        IReadOnlyList<byte[]> reports = ChunkFrameIntoNineByteReports(liveFrame);

        byte[] assembled = HidReportFramer.AssembleInputPayload(reports, requireFramingHeader: true);

        Assert.Equal(liveFrame, assembled);
    }

    [Fact]
    public void HidReportFramer_AssembleInputPayload_PreservesNakAndPartialFramesWithoutCr()
    {
        byte[] nakBytes = [VoltronicProtocolConstants.NakHeader, VoltronicProtocolConstants.CarriageReturn];
        byte[] partialBytes =
        [
            VoltronicProtocolConstants.FrameHeaderHash,
            VoltronicFixtureConstants.Nt2401ProVoltageHexHigh,
            VoltronicFixtureConstants.Nt2401ProVoltageHexLow
        ];

        byte[] nakPayload = HidReportFramer.AssembleInputPayload(ChunkFrameIntoNineByteReports(nakBytes));
        byte[] partialReportChunk =
        [
            HidConstants.DefaultReportId,
            VoltronicProtocolConstants.FrameHeaderHash,
            VoltronicFixtureConstants.Nt2401ProVoltageHexHigh,
            VoltronicFixtureConstants.Nt2401ProVoltageHexLow
        ];
        byte[] partialPayload = HidReportFramer.AssembleInputPayload([partialReportChunk]);

        Assert.Equal(nakBytes, nakPayload);
        Assert.Equal(partialBytes, partialPayload);
    }

    [Fact]
    public void ReadOnlyUpsProbe_BuildCapture_ExtractsTokensFrequencyAndStatusByte2()
    {
        HidDeviceDescriptor descriptor = CreateTestDescriptor();
        byte[] liveFrame = VoltronicFrameMother.Nt2401ProLiveFrame;

        UpsProbeCapture capture = ReadOnlyUpsProbe.BuildCapture(
            descriptor,
            UpsDiagnosticInquiry.QuickStatus,
            ChunkFrameIntoNineByteReports(liveFrame),
            liveFrame);

        Assert.Equal(UpsDiagnosticInquiry.QuickStatus, capture.Inquiry);
        Assert.Equal(VoltronicProtocolConstants.QueryStatusCommand, capture.WireCommand);
        Assert.Equal(11, capture.Tokens.Count);
        Assert.Equal(ExpectedNt2401ProFrequencyHz, capture.FrequencyHz);
        Assert.NotNull(capture.StatusByte2Ratings);
        Assert.Equal(ExpectedNominalFrequencyHz, capture.StatusByte2Ratings.Value.NominalFrequencyHz);
        Assert.Equal(ExpectedNominalBatteryVoltageDc, capture.StatusByte2Ratings.Value.NominalBatteryVoltageDc);
        Assert.Equal(ExpectedNominalOutputVoltageAc, capture.StatusByte2Ratings.Value.NominalOutputVoltageAc);
    }

    [Fact]
    public void ReadOnlyUpsProbe_BuildCapture_StandardAsciiStatus_PreservesLeadingParenAndSkipsBinaryDecode()
    {
        HidDeviceDescriptor descriptor = CreateTestDescriptor();
        byte[] asciiPayload = Encoding.ASCII.GetBytes("(224.0 140.0 224.0 014 59.9 27.2 25.0 00001001\r");
        byte[] originalCopy = (byte[])asciiPayload.Clone();

        UpsProbeCapture capture = ReadOnlyUpsProbe.BuildCapture(
            descriptor,
            UpsDiagnosticInquiry.StandardAsciiStatus,
            ChunkFrameIntoNineByteReports(asciiPayload),
            asciiPayload);

        asciiPayload[0] = (byte)'X';

        Assert.Equal(originalCopy, capture.RawPayload);
        Assert.Equal(originalCopy, capture.DeescapedPayload);
        Assert.Empty(capture.Tokens);
        Assert.Null(capture.FrequencyHz);
        Assert.Null(capture.StatusByte2Ratings);
    }

    [Fact]
    public void ReadOnlyUpsProbe_BuildCapture_ModelDescriptorWithHashPrefix_SkipsBinaryDecode()
    {
        HidDeviceDescriptor descriptor = CreateTestDescriptor();
        byte[] modelPayload = Encoding.ASCII.GetBytes("#NT-2401PRO 2400VA 120V\r");

        UpsProbeCapture capture = ReadOnlyUpsProbe.BuildCapture(
            descriptor,
            UpsDiagnosticInquiry.ModelDescriptor,
            ChunkFrameIntoNineByteReports(modelPayload),
            modelPayload);

        Assert.Empty(capture.Tokens);
        Assert.Null(capture.FrequencyHz);
        Assert.Null(capture.StatusByte2Ratings);
    }

    [Fact]
    public void ReadOnlyUpsProbe_BuildCapture_QuickStatusWithZeroTimerTicks_ReturnsNullFrequency()
    {
        HidDeviceDescriptor descriptor = CreateTestDescriptor();
        byte[] zeroFrequencyFrame = VoltronicBinaryFrameBuilder.Build(
            new VoltronicFrameSpecification
            {
                FrequencyPeriodTicksBytes =
                [
                    VoltronicProtocolConstants.NullByte,
                    VoltronicProtocolConstants.NullByte
                ]
            });

        UpsProbeCapture capture = ReadOnlyUpsProbe.BuildCapture(
            descriptor,
            UpsDiagnosticInquiry.QuickStatus,
            ChunkFrameIntoNineByteReports(zeroFrequencyFrame),
            zeroFrequencyFrame);

        Assert.Null(capture.FrequencyHz);
        Assert.Null(VoltronicParser.TryDecodeFrequencyHz(
            [VoltronicProtocolConstants.NullByte, VoltronicProtocolConstants.NullByte],
            VoltronicBinaryFrameBuilder.EncodeBigEndianBytes(
                VoltronicFixtureConstants.FrequencyClock1228800Hz,
                VoltronicFixtureConstants.FrequencyClockByteWidth)));
    }

    [Fact]
    public void ReadOnlyUpsProbe_BuildCapture_QuickStatusWithLeadingNullByteBeforeHash_DecodesBinaryFrame()
    {
        HidDeviceDescriptor descriptor = CreateTestDescriptor();
        byte[] liveFrame = VoltronicFrameMother.Nt2401ProLiveFrame;
        byte[] paddedPayload = [VoltronicProtocolConstants.NullByte, .. liveFrame];

        UpsProbeCapture capture = ReadOnlyUpsProbe.BuildCapture(
            descriptor,
            UpsDiagnosticInquiry.QuickStatus,
            ChunkFrameIntoNineByteReports(paddedPayload),
            paddedPayload);

        Assert.Equal(11, capture.Tokens.Count);
        Assert.Equal(ExpectedNt2401ProFrequencyHz, capture.FrequencyHz);
        Assert.NotNull(capture.StatusByte2Ratings);
        Assert.Equal(ExpectedNominalFrequencyHz, capture.StatusByte2Ratings.Value.NominalFrequencyHz);
    }

    [Fact]
    public void UpsProbeCaptureFormatter_SerializeJson_ContainsDeviceRawHexTokensAndRatings()
    {
        HidDeviceDescriptor descriptor = CreateTestDescriptor();
        byte[] liveFrame = VoltronicFrameMother.Nt2401ProLiveFrame;

        UpsProbeCapture capture = ReadOnlyUpsProbe.BuildCapture(
            descriptor,
            UpsDiagnosticInquiry.QuickStatus,
            ChunkFrameIntoNineByteReports(liveFrame),
            liveFrame);

        string json = UpsProbeCaptureFormatter.SerializeJson(capture);
        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        Assert.Equal("NT-2401PRO", root.GetProperty("Device").GetProperty("Product").GetString());
        Assert.Equal("QuickStatus", root.GetProperty("Inquiry").GetString());
        Assert.Contains("72 00", root.GetProperty("RawHex").GetString());
        Assert.Contains("12 C0 00", root.GetProperty("RawHex").GetString());
        Assert.True(root.GetProperty("TokenHexBreakdown").GetArrayLength() >= 11);

        JsonElement ratings = root.GetProperty("StatusByte2Ratings");
        Assert.Equal(ExpectedNominalFrequencyHz, ratings.GetProperty("NominalFrequencyHz").GetDouble());
        Assert.Equal(ExpectedNominalBatteryVoltageDc, ratings.GetProperty("NominalBatteryVoltageDc").GetDouble());
        Assert.Equal(ExpectedNominalOutputVoltageAc, ratings.GetProperty("NominalOutputVoltageAc").GetDouble());
    }

    [Theory]
    [InlineData("--inspect", "--cmd", "Q")]
    [InlineData("--inspect", "--cmd")]
    [InlineData("--inspect", "--export")]
    public void InspectCommandDispatch_BlockedOrMissingArguments_ReturnsOneWithoutThrowing(params string[] args)
    {
        ApplicationLaunchOptions options = ApplicationOptionsParser.Parse(args);
        using var output = new StringWriter();
        var strategy = new ApplicationModeStrategyFactory(diagnosticOutput: output).Create(options);

        int exitCode = strategy.Execute();

        Assert.Equal(1, exitCode);
        Assert.False(string.IsNullOrWhiteSpace(output.ToString()));
    }

    private static HidDeviceDescriptor CreateTestDescriptor()
    {
        return new HidDeviceDescriptor(
            @"\\?\HID#VID_0665&PID_5161#test",
            HidConstants.VendorId,
            HidConstants.ProductId,
            VoltronicFixtureConstants.TestDescriptorVersionBcd,
            "Forza",
            "NT-2401PRO",
            "(none)",
            HidConstants.ReportLength,
            HidConstants.ReportLength);
    }

    private static IReadOnlyList<byte[]> ChunkFrameIntoNineByteReports(byte[] payload)
    {
        List<byte[]> reports = new();
        for (int offset = 0; offset < payload.Length; offset += HidConstants.PayloadChunkLength)
        {
            byte[] report = new byte[HidConstants.ReportLength];
            report[0] = HidConstants.DefaultReportId;
            int copyCount = Math.Min(HidConstants.PayloadChunkLength, payload.Length - offset);
            Array.Copy(payload, offset, report, HidConstants.ReportPayloadStartIndex, copyCount);
            reports.Add(report);
        }

        return reports;
    }
}
