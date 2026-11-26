using System;
using VoltTray.Core.Models;
using VoltTray.Infrastructure.Protocol;
using VoltTray.Tests.ObjectMothers;
using Xunit;

namespace VoltTray.Tests;

public class VoltronicParserTests
{
    private readonly VoltronicParser _parser = new();

    [Fact]
    public void Parse_GoldenFrame_DecodesExactTelemetryValues()
    {
        var telemetry = _parser.Parse(VoltronicFrameMother.GoldenFrame);

        Assert.NotNull(telemetry);
        Assert.Equal(117.3, telemetry.InputVoltageAc);
        Assert.Equal(117.3, telemetry.OutputVoltageAc);
        Assert.Equal(14, telemetry.LoadPercentage);
        Assert.Equal(126, telemetry.ActiveLoadWatts); // 14% of 900W = 126W
        Assert.Equal(60.0, telemetry.FrequencyHz);
        Assert.Equal(27.41, telemetry.BatteryVoltageDc);
        Assert.Equal(13.70, telemetry.Cell1VoltageDc);
        Assert.Equal(13.70, telemetry.Cell2VoltageDc);
        Assert.Equal(100, telemetry.BatteryPercentage);
        Assert.Equal(UpsOperationalState.LineNormal, telemetry.State);
        Assert.True(telemetry.IsBeeperMuted); // Bit 0 is 0 -> muted
        Assert.False(telemetry.IsTestInProgress); // Bit 2 is 0 -> false
    }

    [Fact]
    public void Deescape_HandlesAll0x28Sequences()
    {
        byte[] input = [0x28, 0x00, 0x28, 0x01, 0x28, 0x02, 0x28, 0x03, 0x28, 0x04, 0x28, 0x99];
        byte[] expected = [0x0D, 0x11, 0x13, 0x0A, 0x20, 0x99];

        byte[] actual = VoltronicParser.Deescape(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Parse_MalformedFramesAndNak_PreservesPreviousTelemetry()
    {
        var initial = _parser.Parse(VoltronicFrameMother.GoldenFrame);
        Assert.NotNull(initial);

        // NAK frame: N\r
        var resultAfterNak = _parser.Parse(VoltronicFrameMother.CreateNakFrame(), previous: initial);
        Assert.Same(initial, resultAfterNak);

        // Truncated frame
        var resultAfterTruncated = _parser.Parse(VoltronicFrameMother.CreateTruncatedFrame(), previous: initial);
        Assert.Same(initial, resultAfterTruncated);

        // Missing '#' prefix
        byte[] missingPrefix = [0x73, 0x01, 0x20, 0x0D];
        var resultMissingPrefix = _parser.Parse(missingPrefix, previous: initial);
        Assert.Same(initial, resultMissingPrefix);

        // Missing '\r' terminator
        var resultMissingTerminator = _parser.Parse(VoltronicFrameMother.CreateCorruptedMissingCr(), previous: initial);
        Assert.Same(initial, resultMissingTerminator);

        // Null frame
        var resultNull = _parser.Parse(null, previous: initial);
        Assert.Same(initial, resultNull);
    }

    [Theory]
    [InlineData(VoltronicProtocolConstants.StatusFaultMask, UpsOperationalState.Fault)]       // Bit 4
    [InlineData(VoltronicProtocolConstants.StatusBatteryLow, UpsOperationalState.BatteryLow)]  // Bit 7 + Bit 6 on battery
    [InlineData(VoltronicProtocolConstants.StatusOnBattery, UpsOperationalState.OnBattery)]   // Bit 7
    [InlineData(VoltronicProtocolConstants.StatusAvrBoostBuckMask, UpsOperationalState.ChargingAvr)] // Bit 5
    [InlineData(VoltronicProtocolConstants.StatusLineNormal, UpsOperationalState.LineNormal)]  // Bit 3 only
    public void DecodeStatusBits_MapsPrecedenceAccurately(byte statusByte, UpsOperationalState expectedState)
    {
        var (state, _, _) = VoltronicParser.DecodeStatusBits(statusByte, inV: 120.0, outV: 120.0);
        Assert.Equal(expectedState, state);
    }

    [Fact]
    public void DecodeStatusBits_InferredAvrOnMainsDivergence_AssertsChargingAvr()
    {
        // 100V in, 118V out (divergence > 5%) with Bit 5 = 0, Bit 7 = 0
        var (state, _, _) = VoltronicParser.DecodeStatusBits(VoltronicProtocolConstants.StatusLineNormal, inV: 100.0, outV: 118.0);
        Assert.Equal(UpsOperationalState.ChargingAvr, state);
    }

    [Fact]
    public void SelfTestCeiling_Enforces15SecondLimitOnRisingEdge()
    {
        var parser = new VoltronicParser();
        var startTime = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

        // Normal frame before test (Bit 2 = 0)
        byte[] frameIdle = VoltronicFrameMother.CreateIdleFrame();
        var t0 = parser.Parse(frameIdle, now: startTime);
        Assert.False(t0!.IsTestInProgress);

        // Test starts: rising edge (Bit 2 = 1) at t = 0s
        byte[] frameTesting = VoltronicFrameMother.CreateSelfTestFrame();
        var t1 = parser.Parse(frameTesting, now: startTime);
        Assert.True(t1!.IsTestInProgress);

        // Telemetry at t = 10s: test still in progress (< 15s)
        var t2 = parser.Parse(frameTesting, now: startTime.AddSeconds(10));
        Assert.True(t2!.IsTestInProgress);

        // Telemetry at t = 15s: ceiling reached, test cleared unconditionally
        var t3 = parser.Parse(frameTesting, now: startTime.AddSeconds(15));
        Assert.False(t3!.IsTestInProgress);

        // Telemetry at t = 20s: Bit 2 is stuck at 1, but must NOT re-arm
        var t4 = parser.Parse(frameTesting, now: startTime.AddSeconds(20));
        Assert.False(t4!.IsTestInProgress);

        // Bit 2 returns to 0 at t = 25s
        var t5 = parser.Parse(frameIdle, now: startTime.AddSeconds(25));
        Assert.False(t5!.IsTestInProgress);

        // New test starts at t = 30s: new rising edge arms test successfully
        var t6 = parser.Parse(frameTesting, now: startTime.AddSeconds(30));
        Assert.True(t6!.IsTestInProgress);
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
    public void DecodeBigEndianUInt64_HandlesOneToEightBytesAndBounds(byte[] input, ulong expected) =>
        Assert.Equal(expected, VoltronicParser.DecodeBigEndianUInt64(input));

    [Fact]
    public void Parse_ArbitraryWidthFrequencyFrames_DecodesExpectedHz()
    {
        var nt2401 = _parser.Parse(VoltronicFrameMother.Nt2401ProLiveFrame)!;
        Assert.Equal(116.2, nt2401.InputVoltageAc);
        Assert.Equal(59.97, nt2401.FrequencyHz);
        Assert.False(nt2401.IsBeeperMuted);
        Assert.Equal(50.08, _parser.Parse(VoltronicFrameMother.Upstream50HzFrame)!.FrequencyHz);
        Assert.Equal(60.0, _parser.Parse(VoltronicFrameMother.Dsp4ByteTimerFrame)!.FrequencyHz);
        Assert.Equal(59.97, VoltronicParser.DecodeFrequencyHz([0x50, 0x0B], [0x12, 0xC0, 0x00]));
        Assert.Equal(60.0, VoltronicParser.DecodeFrequencyHz([0x50, 0x0C], [0x12, 0xC0]));
    }

    [Theory]
    [InlineData((byte)0xA1, 60.0, 2, 24.0, 120.0)]
    [InlineData((byte)0x02, 50.0, 1, 12.0, 220.0)]
    [InlineData((byte)0x63, 50.0, 4, 48.0, 230.0)]
    [InlineData((byte)0x47, 50.0, 3, 36.0, null)]
    public void DecodeStatusByte2Ratings_ExtractsFrequencyBankAndNominalAc(byte status2, double hz, int banks, double dc, double? ac)
    {
        var ratings = VoltronicParser.DecodeStatusByte2Ratings(status2);
        Assert.Equal((hz, banks, dc, ac), (ratings.NominalFrequencyHz, ratings.BatteryBankSeriesCount, ratings.NominalBatteryVoltageDc, ratings.NominalOutputVoltageAc));
    }
}
