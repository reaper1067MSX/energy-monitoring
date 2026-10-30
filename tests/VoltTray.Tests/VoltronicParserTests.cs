using System;
using System.Text;
using VoltTray.Core.Models;
using VoltTray.Core.Services;
using Xunit;

namespace VoltTray.Tests;

public class VoltronicParserTests
{
    private readonly VoltronicParser _parser = new();

    // Golden frame bytes from spec_miner_physics/handoff.md
    private static readonly byte[] GoldenFrame =
    [
        0x23, 0x73, 0x01, 0x20, 0x34, 0x20, 0x73, 0x01, 0x20, 0x34, 0x20,
        0x0E, 0x20, 0x50, 0x0C, 0x20, 0x12, 0xC0, 0x20, 0xE9, 0x20, 0x3C, 0x20,
        0x08, 0x20, 0xA1, 0x0D
    ];

    [Fact]
    public void Parse_GoldenFrame_DecodesExactTelemetryValues()
    {
        var telemetry = _parser.Parse(GoldenFrame);

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
        var initial = _parser.Parse(GoldenFrame);
        Assert.NotNull(initial);

        // NAK frame: N\r
        byte[] nakFrame = [0x4E, 0x0D];
        var resultAfterNak = _parser.Parse(nakFrame, previous: initial);
        Assert.Same(initial, resultAfterNak);

        // Truncated frame
        byte[] truncated = [0x23, 0x73, 0x01, 0x0D];
        var resultAfterTruncated = _parser.Parse(truncated, previous: initial);
        Assert.Same(initial, resultAfterTruncated);

        // Missing '#' prefix
        byte[] missingPrefix = [0x73, 0x01, 0x20, 0x0D];
        var resultMissingPrefix = _parser.Parse(missingPrefix, previous: initial);
        Assert.Same(initial, resultMissingPrefix);

        // Missing '\r' terminator
        byte[] missingTerminator = [0x23, 0x73, 0x01];
        var resultMissingTerminator = _parser.Parse(missingTerminator, previous: initial);
        Assert.Same(initial, resultMissingTerminator);

        // Null frame
        var resultNull = _parser.Parse(null, previous: initial);
        Assert.Same(initial, resultNull);
    }

    [Theory]
    [InlineData(0x10, UpsOperationalState.Fault)]       // Bit 4
    [InlineData(0xC0, UpsOperationalState.BatteryLow)]  // Bit 7 + Bit 6 on battery
    [InlineData(0x80, UpsOperationalState.OnBattery)]   // Bit 7
    [InlineData(0x20, UpsOperationalState.ChargingAvr)] // Bit 5
    [InlineData(0x08, UpsOperationalState.LineNormal)]  // Bit 3 only
    public void DecodeStatusBits_MapsPrecedenceAccurately(byte statusByte, UpsOperationalState expectedState)
    {
        var (state, _, _) = VoltronicParser.DecodeStatusBits(statusByte, inV: 120.0, outV: 120.0);
        Assert.Equal(expectedState, state);
    }

    [Fact]
    public void DecodeStatusBits_InferredAvrOnMainsDivergence_AssertsChargingAvr()
    {
        // 100V in, 118V out (divergence > 5%) with Bit 5 = 0, Bit 7 = 0
        var (state, _, _) = VoltronicParser.DecodeStatusBits(0x08, inV: 100.0, outV: 118.0);
        Assert.Equal(UpsOperationalState.ChargingAvr, state);
    }

    [Fact]
    public void SelfTestCeiling_Enforces15SecondLimitOnRisingEdge()
    {
        var parser = new VoltronicParser();
        var startTime = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

        // Normal frame before test (Bit 2 = 0)
        byte[] frameIdle = (byte[])GoldenFrame.Clone();
        frameIdle[23] = 0x08; // Bit 2 = 0
        var t0 = parser.Parse(frameIdle, now: startTime);
        Assert.False(t0!.IsTestInProgress);

        // Test starts: rising edge (Bit 2 = 1) at t = 0s
        byte[] frameTesting = (byte[])GoldenFrame.Clone();
        frameTesting[23] = 0x0C; // Bit 3=1, Bit 2=1 (0x0C)
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
}
