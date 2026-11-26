using System;
using System.Collections.Generic;
using VoltTray.Core.Models;
using VoltTray.Infrastructure.Protocol;
using VoltTray.Tests.ObjectMothers;
using Xunit;

namespace VoltTray.Tests;

public class VoltronicParserTests
{
    private const double ExpectedGoldenInputVoltageAc = 117.3;
    private const double ExpectedGoldenOutputVoltageAc = 117.3;
    private const int ExpectedGoldenLoadPercentage = 14;
    private const int ExpectedGoldenActiveLoadWatts = 126;
    private const double ExpectedGoldenFrequencyHz = 60.0;
    private const double ExpectedGoldenBatteryVoltageDc = 27.41;
    private const double ExpectedGoldenCellVoltageDc = 13.70;
    private const int ExpectedGoldenBatteryPercentage = 100;
    private const double ExpectedNt2401ProInputVoltageAc = 116.2;
    private const double ExpectedNt2401ProFrequencyHz = 59.97;
    private const double ExpectedUpstream50HzFrequencyHz = 50.08;
    private const int GoldenFrameExpectedLength = 27;
    private const int Nt2401ProFrameExpectedLength = 28;

    private readonly VoltronicParser _parser = new();

    [Fact]
    public void Parse_GoldenFrame_DecodesExactTelemetryValues()
    {
        UpsTelemetry? telemetry = _parser.Parse(VoltronicFrameMother.GoldenFrame);

        Assert.NotNull(telemetry);
        Assert.Equal(ExpectedGoldenInputVoltageAc, telemetry.InputVoltageAc);
        Assert.Equal(ExpectedGoldenOutputVoltageAc, telemetry.OutputVoltageAc);
        Assert.Equal(ExpectedGoldenLoadPercentage, telemetry.LoadPercentage);
        Assert.Equal(ExpectedGoldenActiveLoadWatts, telemetry.ActiveLoadWatts);
        Assert.Equal(ExpectedGoldenFrequencyHz, telemetry.FrequencyHz);
        Assert.Equal(ExpectedGoldenBatteryVoltageDc, telemetry.BatteryVoltageDc);
        Assert.Equal(ExpectedGoldenCellVoltageDc, telemetry.Cell1VoltageDc);
        Assert.Equal(ExpectedGoldenCellVoltageDc, telemetry.Cell2VoltageDc);
        Assert.Equal(ExpectedGoldenBatteryPercentage, telemetry.BatteryPercentage);
        Assert.Equal(UpsOperationalState.LineNormal, telemetry.State);
        Assert.True(telemetry.IsBeeperMuted);
        Assert.False(telemetry.IsTestInProgress);
    }

    [Fact]
    public void Deescape_HandlesAll0x28Sequences()
    {
        const byte unmappedEscapeValue = 0x99;
        byte[] input =
        [
            VoltronicProtocolConstants.FrameHeaderParen, VoltronicProtocolConstants.EscapeCodeCr,
            VoltronicProtocolConstants.FrameHeaderParen, VoltronicProtocolConstants.EscapeCodeDc1,
            VoltronicProtocolConstants.FrameHeaderParen, VoltronicProtocolConstants.EscapeCodeDc3,
            VoltronicProtocolConstants.FrameHeaderParen, VoltronicProtocolConstants.EscapeCodeLf,
            VoltronicProtocolConstants.FrameHeaderParen, VoltronicProtocolConstants.EscapeCodeSpace,
            VoltronicProtocolConstants.FrameHeaderParen, unmappedEscapeValue
        ];
        byte[] expected =
        [
            VoltronicProtocolConstants.CarriageReturn,
            VoltronicProtocolConstants.Dc1,
            VoltronicProtocolConstants.Dc3,
            VoltronicProtocolConstants.LineFeed,
            VoltronicProtocolConstants.SpaceDelimiter,
            unmappedEscapeValue
        ];

        byte[] actual = VoltronicParser.Deescape(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Parse_MalformedFramesAndNak_PreservesPreviousTelemetry()
    {
        UpsTelemetry? initialTelemetry = _parser.Parse(VoltronicFrameMother.GoldenFrame);
        Assert.NotNull(initialTelemetry);

        UpsTelemetry? resultAfterNak = _parser.Parse(VoltronicFrameMother.CreateNakFrame(), previous: initialTelemetry);
        Assert.Same(initialTelemetry, resultAfterNak);

        UpsTelemetry? resultAfterTruncated = _parser.Parse(
            VoltronicFrameMother.CreateTruncatedFrame(),
            previous: initialTelemetry);
        Assert.Same(initialTelemetry, resultAfterTruncated);

        byte[] missingPrefix =
        [
            VoltronicProtocolConstants.NominalInputVoltageHexHigh,
            VoltronicProtocolConstants.NominalInputVoltageHexLow,
            VoltronicProtocolConstants.SpaceDelimiter,
            VoltronicProtocolConstants.CarriageReturn
        ];
        UpsTelemetry? resultMissingPrefix = _parser.Parse(missingPrefix, previous: initialTelemetry);
        Assert.Same(initialTelemetry, resultMissingPrefix);

        UpsTelemetry? resultMissingTerminator = _parser.Parse(
            VoltronicFrameMother.CreateCorruptedMissingCr(),
            previous: initialTelemetry);
        Assert.Same(initialTelemetry, resultMissingTerminator);

        UpsTelemetry? resultNull = _parser.Parse(null, previous: initialTelemetry);
        Assert.Same(initialTelemetry, resultNull);
    }

    [Theory]
    [InlineData(VoltronicProtocolConstants.StatusFaultMask, UpsOperationalState.Fault)]
    [InlineData(VoltronicFixtureConstants.StatusBatteryLow, UpsOperationalState.BatteryLow)]
    [InlineData(VoltronicFixtureConstants.StatusOnBattery, UpsOperationalState.OnBattery)]
    [InlineData(VoltronicProtocolConstants.StatusAvrBoostBuckMask, UpsOperationalState.ChargingAvr)]
    [InlineData(VoltronicProtocolConstants.StatusLineNormal, UpsOperationalState.LineNormal)]
    public void DecodeStatusBits_MapsPrecedenceAccurately(byte statusByte, UpsOperationalState expectedState)
    {
        var (state, _, _) = VoltronicParser.DecodeStatusBits(
            statusByte,
            inV: 120.0,
            outV: 120.0);
        Assert.Equal(expectedState, state);
    }

    [Fact]
    public void DecodeStatusBits_InferredAvrOnMainsDivergence_AssertsChargingAvr()
    {
        var (state, _, _) = VoltronicParser.DecodeStatusBits(
            VoltronicProtocolConstants.StatusLineNormal,
            inV: 100.0,
            outV: 118.0);
        Assert.Equal(UpsOperationalState.ChargingAvr, state);
    }

    [Fact]
    public void SelfTestCeiling_Enforces15SecondLimitOnRisingEdge()
    {
        var parser = new VoltronicParser();
        var startTime = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

        byte[] idleFrame = VoltronicFrameMother.CreateIdleFrame();
        UpsTelemetry? baselineTelemetry = parser.Parse(idleFrame, now: startTime);
        Assert.False(baselineTelemetry!.IsTestInProgress);

        byte[] selfTestFrame = VoltronicFrameMother.CreateSelfTestFrame();
        UpsTelemetry? selfTestStartTelemetry = parser.Parse(selfTestFrame, now: startTime);
        Assert.True(selfTestStartTelemetry!.IsTestInProgress);

        UpsTelemetry? selfTestAt10SecondsTelemetry = parser.Parse(selfTestFrame, now: startTime.AddSeconds(10));
        Assert.True(selfTestAt10SecondsTelemetry!.IsTestInProgress);

        UpsTelemetry? selfTestAt15SecondsTelemetry = parser.Parse(selfTestFrame, now: startTime.AddSeconds(15));
        Assert.False(selfTestAt15SecondsTelemetry!.IsTestInProgress);

        UpsTelemetry? selfTestAt20SecondsTelemetry = parser.Parse(selfTestFrame, now: startTime.AddSeconds(20));
        Assert.False(selfTestAt20SecondsTelemetry!.IsTestInProgress);

        UpsTelemetry? recoveredIdleTelemetry = parser.Parse(idleFrame, now: startTime.AddSeconds(25));
        Assert.False(recoveredIdleTelemetry!.IsTestInProgress);

        UpsTelemetry? secondSelfTestTelemetry = parser.Parse(selfTestFrame, now: startTime.AddSeconds(30));
        Assert.True(secondSelfTestTelemetry!.IsTestInProgress);
    }

    [Theory]
    [InlineData(new byte[] { 0x34 }, 0x34UL)]
    [InlineData(new byte[] { 0x50, 0x0B }, 0x500BUL)]
    [InlineData(new byte[] { 0x12, 0xC0, 0x00 }, 0x12C000UL)]
    [InlineData(new byte[] { 0x05, 0xDC, 0x00, 0x00 }, 0x05DC0000UL)]
    [InlineData(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 }, 0x0102030405UL)]
    [InlineData(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 }, 0x0102030405060708UL)]
    [InlineData(new byte[0], 0UL)]
    [InlineData(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, 0UL)]
    public void DecodeBigEndianUInt64_HandlesOneToEightBytesAndBounds(byte[] input, ulong expected)
    {
        ulong decodedValue = VoltronicParser.DecodeBigEndianUInt64(input);
        Assert.Equal(expected, decodedValue);
    }

    [Fact]
    public void Parse_ArbitraryWidthFrequencyFrames_DecodesExpectedHz()
    {
        UpsTelemetry? nt2401Telemetry = _parser.Parse(VoltronicFrameMother.Nt2401ProLiveFrame);
        Assert.NotNull(nt2401Telemetry);
        Assert.Equal(ExpectedNt2401ProInputVoltageAc, nt2401Telemetry.InputVoltageAc);
        Assert.Equal(ExpectedNt2401ProFrequencyHz, nt2401Telemetry.FrequencyHz);
        Assert.False(nt2401Telemetry.IsBeeperMuted);

        UpsTelemetry? upstream50HzTelemetry = _parser.Parse(VoltronicFrameMother.Upstream50HzFrame);
        Assert.NotNull(upstream50HzTelemetry);
        Assert.Equal(ExpectedUpstream50HzFrequencyHz, upstream50HzTelemetry.FrequencyHz);

        UpsTelemetry? dsp4ByteTelemetry = _parser.Parse(VoltronicFrameMother.Dsp4ByteTimerFrame);
        Assert.NotNull(dsp4ByteTelemetry);
        Assert.Equal(ExpectedGoldenFrequencyHz, dsp4ByteTelemetry.FrequencyHz);

        byte[] nt2401PeriodBytes = VoltronicBinaryFrameBuilder.EncodeBigEndianBytes(
            VoltronicFixtureConstants.Nt2401ProFrequencyPeriodTicks,
            VoltronicFixtureConstants.FrequencyTicks2ByteWidth);
        byte[] clockBytes = VoltronicBinaryFrameBuilder.EncodeBigEndianBytes(
            VoltronicFixtureConstants.FrequencyClock1228800Hz,
            VoltronicFixtureConstants.FrequencyClockByteWidth);

        Assert.Equal(ExpectedNt2401ProFrequencyHz, VoltronicParser.DecodeFrequencyHz(nt2401PeriodBytes, clockBytes));
        Assert.Equal(
            ExpectedGoldenFrequencyHz,
            VoltronicParser.DecodeFrequencyHz(
                [VoltronicProtocolConstants.NominalFrequencyPart1High, VoltronicProtocolConstants.NominalFrequencyPart1Low],
                [VoltronicProtocolConstants.NominalFrequencyPart2High, VoltronicProtocolConstants.NominalFrequencyPart2Low]));
    }

    [Fact]
    public void VoltronicFrameMother_ProducesExactByteParityForGoldenAndNt2401ProFrames()
    {
        byte[] expectedGolden =
        [
            0x23, 0x73, 0x01, 0x20, 0x34, 0x20, 0x73, 0x01, 0x20, 0x34, 0x20,
            0x0E, 0x20, 0x50, 0x0C, 0x20, 0x12, 0xC0, 0x20, 0xE9, 0x20, 0x3C, 0x20,
            0x08, 0x20, 0xA1, 0x0D
        ];

        byte[] expectedNt2401Pro =
        [
            0x23, 0x72, 0x00, 0x20, 0x34, 0x20, 0x72, 0x00, 0x20, 0x34, 0x20,
            0x0C, 0x20, 0x50, 0x0B, 0x20, 0x12, 0xC0, 0x00, 0x20, 0xE9, 0x20,
            0x3C, 0x20, 0x09, 0x20, 0xA1, 0x0D
        ];

        Assert.Equal(GoldenFrameExpectedLength, VoltronicFrameMother.GoldenFrame.Length);
        Assert.Equal(expectedGolden, VoltronicFrameMother.GoldenFrame);
        Assert.Equal(Nt2401ProFrameExpectedLength, VoltronicFrameMother.Nt2401ProLiveFrame.Length);
        Assert.Equal(expectedNt2401Pro, VoltronicFrameMother.Nt2401ProLiveFrame);
        Assert.NotSame(VoltronicFrameMother.GoldenFrame, VoltronicFrameMother.GoldenFrame);
        Assert.NotSame(VoltronicFrameMother.Nt2401ProLiveFrame, VoltronicFrameMother.Nt2401ProLiveFrame);
    }

    [Theory]
    [InlineData(VoltronicProtocolConstants.StatusByte2Nominal, 60.0, 2, 24.0, 120.0)]
    [InlineData(VoltronicFixtureConstants.StatusByte2European12V50Hz220V, 50.0, 1, 12.0, 220.0)]
    [InlineData(VoltronicFixtureConstants.StatusByte2Nominal48V50Hz230V, 50.0, 4, 48.0, 230.0)]
    [InlineData(VoltronicFixtureConstants.StatusByte2Nominal36V50HzUnknownAc, 50.0, 3, 36.0, null)]
    public void DecodeStatusByte2Ratings_ExtractsFrequencyBankAndNominalAc(
        byte statusByte2,
        double expectedFrequencyHz,
        int expectedBatteryBanks,
        double expectedBatteryVoltageDc,
        double? expectedOutputVoltageAc)
    {
        UpsStatusByte2Ratings ratings = VoltronicParser.DecodeStatusByte2Ratings(statusByte2);

        Assert.Equal(expectedFrequencyHz, ratings.NominalFrequencyHz);
        Assert.Equal(expectedBatteryBanks, ratings.BatteryBankSeriesCount);
        Assert.Equal(expectedBatteryVoltageDc, ratings.NominalBatteryVoltageDc);
        Assert.Equal(expectedOutputVoltageAc, ratings.NominalOutputVoltageAc);
    }

    [Fact]
    public void TryTokenizeHashFrame_SplitsAndDeescapesValidFrameAndRejectsMalformed()
    {
        byte[] escapedFrame =
        [
            VoltronicProtocolConstants.FrameHeaderHash,
            VoltronicProtocolConstants.NominalInputVoltageHexHigh,
            VoltronicProtocolConstants.FrameHeaderParen,
            VoltronicProtocolConstants.EscapeCodeSpace,
            VoltronicProtocolConstants.SpaceDelimiter,
            VoltronicProtocolConstants.NominalInputVoltageRatio,
            VoltronicProtocolConstants.SpaceDelimiter,
            VoltronicProtocolConstants.FrameHeaderParen,
            VoltronicProtocolConstants.EscapeCodeCr,
            VoltronicProtocolConstants.CarriageReturn
        ];

        bool validResult = VoltronicParser.TryTokenizeHashFrame(escapedFrame, out IReadOnlyList<byte[]> validTokens);
        Assert.True(validResult);
        Assert.Equal(3, validTokens.Count);
        Assert.Equal(
            [VoltronicProtocolConstants.NominalInputVoltageHexHigh, VoltronicProtocolConstants.SpaceDelimiter],
            validTokens[0]);
        Assert.Equal([VoltronicProtocolConstants.NominalInputVoltageRatio], validTokens[1]);
        Assert.Equal([VoltronicProtocolConstants.CarriageReturn], validTokens[2]);

        bool invalidResult = VoltronicParser.TryTokenizeHashFrame(
            VoltronicFrameMother.CreateCorruptedMissingCr(),
            out IReadOnlyList<byte[]> _);
        Assert.False(invalidResult);
    }

    [Fact]
    public void TryDecodeFrequencyHz_ReturnsNullForInvalidOrZeroInputsAndValueForValidTokens()
    {
        byte[] nt2401PeriodBytes = VoltronicBinaryFrameBuilder.EncodeBigEndianBytes(
            VoltronicFixtureConstants.Nt2401ProFrequencyPeriodTicks,
            VoltronicFixtureConstants.FrequencyTicks2ByteWidth);
        byte[] clockBytes = VoltronicBinaryFrameBuilder.EncodeBigEndianBytes(
            VoltronicFixtureConstants.FrequencyClock1228800Hz,
            VoltronicFixtureConstants.FrequencyClockByteWidth);

        Assert.Null(VoltronicParser.TryDecodeFrequencyHz([], clockBytes));
        Assert.Null(VoltronicParser.TryDecodeFrequencyHz([VoltronicProtocolConstants.NullByte, VoltronicProtocolConstants.NullByte], clockBytes));
        Assert.Null(VoltronicParser.TryDecodeFrequencyHz(
            [VoltronicProtocolConstants.NominalFrequencyPart1High, VoltronicProtocolConstants.NominalFrequencyPart1Low],
            [VoltronicProtocolConstants.NominalFrequencyPart2High, VoltronicProtocolConstants.NominalFrequencyPart2Low]));
        Assert.Equal(ExpectedNt2401ProFrequencyHz, VoltronicParser.TryDecodeFrequencyHz(nt2401PeriodBytes, clockBytes));
    }

    [Fact]
    public void VoltronicFrameMother_CreateAvrFrame_EncodesProvidedInputAndOutputVoltages()
    {
        const double expectedInputVoltageAc = 104.5;
        const double expectedOutputVoltageAc = 119.8;

        byte[] avrFrame = VoltronicFrameMother.CreateAvrFrame(expectedInputVoltageAc, expectedOutputVoltageAc);
        UpsTelemetry? telemetry = _parser.Parse(avrFrame);

        Assert.NotNull(telemetry);
        Assert.Equal(UpsOperationalState.ChargingAvr, telemetry.State);
        Assert.Equal(expectedInputVoltageAc, telemetry.InputVoltageAc);
        Assert.Equal(expectedOutputVoltageAc, telemetry.OutputVoltageAc);
    }
}
