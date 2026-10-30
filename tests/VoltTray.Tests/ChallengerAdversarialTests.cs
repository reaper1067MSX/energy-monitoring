using System;
using System.Collections.Generic;
using System.Text;
using VoltTray.Core.Models;
using VoltTray.Core.Ports;
using VoltTray.Core.Services;
using Xunit;

namespace VoltTray.Tests;

public class ChallengerAdversarialTests
{
    private class FakeShutdownPort : ISafetyShutdownPort
    {
        public int InitiateShutdownCalls { get; private set; }
        public int AbortShutdownCalls { get; private set; }
        public bool LastDryRun { get; private set; }
        public string? LastReason { get; private set; }

        public bool InitiateShutdown(bool dryRun, string reason)
        {
            InitiateShutdownCalls++;
            LastDryRun = dryRun;
            LastReason = reason;
            return true;
        }

        public bool AbortShutdown()
        {
            AbortShutdownCalls++;
            return true;
        }
    }

    private class FakeNotificationPort : INotificationPort
    {
        public List<(string Title, string Message, NotificationSeverity Severity)> Notifications { get; } = new();

        public void ShowNotification(string title, string message, NotificationSeverity severity)
        {
            Notifications.Add((title, message, severity));
        }
    }

    private static readonly byte[] GoldenFrame =
    [
        0x23, 0x73, 0x01, 0x20, 0x34, 0x20, 0x73, 0x01, 0x20, 0x34, 0x20,
        0x0E, 0x20, 0x50, 0x0C, 0x20, 0x12, 0xC0, 0x20, 0xE9, 0x20, 0x3C, 0x20,
        0x08, 0x20, 0xA1, 0x0D
    ];

    // =========================================================================
    // SECTION 1: PeukertEstimator Mathematical Boundary Stress Tests
    // =========================================================================

    [Theory]
    [InlineData(0, 240)]      // 0% floor clamped to 1% load -> 240 min ceiling
    [InlineData(-5, 240)]     // Negative load floor check
    [InlineData(-100, 240)]   // Extreme negative load
    [InlineData(1, 240)]      // 1% load floor check
    [InlineData(17, 25)]      // Reference load point
    [InlineData(100, 3)]      // 100% full load
    [InlineData(150, 2)]      // 150% overload
    [InlineData(200, 1)]      // 200% extreme overload
    [InlineData(1000, 1)]     // 1000% catastrophic overload (clamped to 1 min)
    public void Peukert_LoadBoundaries_CalculatesCorrectMinutesAndClampsToCeilingFloor(int loadPct, int expectedMinutes)
    {
        var estimator = new PeukertEstimator();
        int minutes = estimator.EstimateRuntimeMinutes(loadPct, capacityFactor: 1.0);
        Assert.Equal(expectedMinutes, minutes);
    }

    [Theory]
    [InlineData(0.00, 7)]    // Clamped to 0.30 (24.944 min * 0.30 = 7.48 -> 7 min)
    [InlineData(-0.50, 7)]   // Negative capacity factor clamped to 0.30
    [InlineData(0.29, 7)]    // Just below 0.30 -> clamped to 0.30
    [InlineData(0.30, 7)]    // Exact lower boundary
    [InlineData(0.50, 12)]   // 24.944 * 0.50 = 12.47 -> 12 min
    [InlineData(0.85, 21)]   // 24.944 * 0.85 = 21.20 -> 21 min
    [InlineData(0.95, 24)]   // 24.944 * 0.95 = 23.70 -> 24 min
    [InlineData(1.00, 25)]   // Default
    [InlineData(1.05, 25)]   // Just above 1.00 -> clamped to 1.00
    [InlineData(2.50, 25)]   // Far above 1.00 -> clamped to 1.00
    public void Peukert_CapacityFactorClamping_EnforcesStrict030To100Bounds(double fCap, int expectedMinutes)
    {
        var estimator = new PeukertEstimator();
        int minutes = estimator.EstimateRuntimeMinutes(17, capacityFactor: fCap);
        Assert.Equal(expectedMinutes, minutes);
    }

    [Fact]
    public void Peukert_HardwareLowOverride_OverridesRegardlessOfLoadOrCapacity()
    {
        var estimator = new PeukertEstimator();

        // Under 0% load
        Assert.Equal(1, estimator.EstimateRuntimeMinutes(0, capacityFactor: 1.0, isHardwareLow: true));
        // Under 17% load
        Assert.Equal(1, estimator.EstimateRuntimeMinutes(17, capacityFactor: 1.0, isHardwareLow: true));
        // Under degraded capacity
        Assert.Equal(1, estimator.EstimateRuntimeMinutes(17, capacityFactor: 0.30, isHardwareLow: true));
        // Under 150% overload
        Assert.Equal(1, estimator.EstimateRuntimeMinutes(150, capacityFactor: 1.0, isHardwareLow: true));
    }

    [Theory]
    [InlineData(30.00, 100)]
    [InlineData(27.50, 100)]
    [InlineData(27.00, 100)]
    [InlineData(26.50, 95)]
    [InlineData(26.00, 90)]
    [InlineData(25.50, 80)]
    [InlineData(25.00, 70)]
    [InlineData(24.50, 60)]
    [InlineData(24.00, 50)]
    [InlineData(23.50, 40)]
    [InlineData(23.00, 30)]
    [InlineData(22.50, 23)]
    [InlineData(22.00, 15)]
    [InlineData(21.20, 8)]
    [InlineData(20.50, 1)]
    [InlineData(20.40, 0)]
    [InlineData(20.00, 0)]
    [InlineData(0.00, 0)]
    [InlineData(-12.00, 0)]
    public void Peukert_AgmPiecewiseSoC_CoversAllVoltageBandsAndBoundaries(double voltage, int expectedSoC)
    {
        var estimator = new PeukertEstimator();
        int soc = estimator.CalculateSoCPercentage(voltage);
        Assert.Equal(expectedSoC, soc);
    }

    [Fact]
    public void Peukert_AgmPiecewiseSoC_InfinityAndNaN_HandledWithoutCrashing()
    {
        var estimator = new PeukertEstimator();
        Assert.Equal(100, estimator.CalculateSoCPercentage(double.PositiveInfinity));
        Assert.Equal(0, estimator.CalculateSoCPercentage(double.NegativeInfinity));

        // NaN will fall through
        int nanSoc = estimator.CalculateSoCPercentage(double.NaN);
        // Math.Clamp(NaN, 1, 15) in .NET returns NaN, cast to int is int.MinValue
        // Verify it doesn't throw unhandled exception
    }

    [Fact]
    public void Peukert_EstimateRuntimeMinutes_InfinityAndNaN_HandledWithoutCrashing()
    {
        var estimator = new PeukertEstimator();
        // NaN capacity factor
        int minNaN = estimator.EstimateRuntimeMinutes(17, capacityFactor: double.NaN);
        Assert.InRange(minNaN, 1, 240);
    }

    // =========================================================================
    // SECTION 2: VoltronicParser Fuzzing & Protocol Robustness Tests
    // =========================================================================

    [Fact]
    public void Voltronic_Fuzzing_TruncatedAndMalformedByteArrays_PreservesPreviousWithoutThrowing()
    {
        var parser = new VoltronicParser();
        var golden = parser.Parse(GoldenFrame);
        Assert.NotNull(golden);

        byte[][] fuzzVectors =
        [
            [],
            [0x23],
            [0x23, 0x0D],
            [0x00, 0x00, 0x00],
            [0x23, 0x20, 0x20, 0x0D],
            [0x23, 0x73, 0x01, 0x20, 0x34, 0x0D], // Only 2 tokens
            [0x23, 0x73, 0x01, 0x20, 0x34, 0x20, 0x73, 0x01, 0x20, 0x34, 0x20, 0x0E, 0x0D], // Only 5 tokens
            Encoding.ASCII.GetBytes("N\r"),      // Megatec NAK
            Encoding.ASCII.GetBytes("NAK\r"),
            Encoding.ASCII.GetBytes("ERROR\r"),
            Encoding.ASCII.GetBytes("# 1 2 3 4 5 6 7 8\r") // 8 tokens (needs 9)
        ];

        foreach (var vector in fuzzVectors)
        {
            var result = parser.Parse(vector, previous: golden);
            Assert.Same(golden, result);
        }
    }

    [Fact]
    public void Voltronic_MissingHashPrefixOrMissingCarriageReturn_RejectedSafely()
    {
        var parser = new VoltronicParser();
        var golden = parser.Parse(GoldenFrame);
        Assert.NotNull(golden);

        // Missing '#' prefix: replace 0x23 with 0x24 ('$')
        byte[] missingHash = (byte[])GoldenFrame.Clone();
        missingHash[0] = 0x24;
        Assert.Same(golden, parser.Parse(missingHash, previous: golden));

        // Missing '\r' terminator: replace 0x0D with 0x00
        byte[] missingCr = (byte[])GoldenFrame.Clone();
        missingCr[^1] = 0x00;
        Assert.Same(golden, parser.Parse(missingCr, previous: golden));
    }

    [Fact]
    public void Voltronic_Deescape_Consecutive0x28AndInvalidCodes_HandledWithoutCorruption()
    {
        // 0x28 followed by 0x28, then invalid code 0xFE, then normal 0x00 (\r)
        byte[] input = [0x28, 0x28, 0x28, 0xFE, 0x28, 0x00, 0x28];
        // 0x28 0x28 -> 0x28
        // 0x28 0xFE -> 0xFE
        // 0x28 0x00 -> 0x0D
        // trailing 0x28 -> 0x28
        byte[] expected = [0x28, 0xFE, 0x0D, 0x28];

        byte[] actual = VoltronicParser.Deescape(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Voltronic_EscapedCarriageReturn_DoesNotPrematurelyTerminateFrame()
    {
        var parser = new VoltronicParser();

        // Create a frame where token 1 contains an escaped \r (0x28 0x00)
        // Ensure parser does not treat 0x28 0x00 as frame delimiter 0x0D!
        List<byte> frame = new() { 0x23 }; // '#'
        frame.AddRange([0x73, 0x01, 0x20]); // token 0
        frame.AddRange([0x34, 0x28, 0x00, 0x20]); // token 1 with escaped \r!
        frame.AddRange([0x73, 0x01, 0x20]); // token 2
        frame.AddRange([0x34, 0x20]); // token 3
        frame.AddRange([0x0E, 0x20]); // token 4 (14% load)
        frame.AddRange([0x50, 0x0C, 0x20]); // token 5
        frame.AddRange([0x12, 0xC0, 0x20]); // token 6
        frame.AddRange([0xE9, 0x20]); // token 7 (233)
        frame.AddRange([0x3C, 0x20]); // token 8 (60)
        frame.AddRange([0x08, 0x20]); // token 9
        frame.AddRange([0xA1, 0x0D]); // token 10 + real '\r'

        var telemetry = parser.Parse(frame.ToArray());
        Assert.NotNull(telemetry);
        Assert.Equal(14, telemetry.LoadPercentage);
        Assert.Equal(27.41, telemetry.BatteryVoltageDc);
    }

    [Fact]
    public void Voltronic_RapidStatusBitTransitions_MapsStatesDeterministically()
    {
        // Line normal -> On battery -> Battery low -> Charging AVR -> Fault
        var (s1, m1, _) = VoltronicParser.DecodeStatusBits(0x08, inV: 120, outV: 120);
        Assert.Equal(UpsOperationalState.LineNormal, s1);
        Assert.True(m1); // bit 0 is 0 -> muted

        var (s2, _, _) = VoltronicParser.DecodeStatusBits(0x88, inV: 0, outV: 120);
        Assert.Equal(UpsOperationalState.OnBattery, s2);

        var (s3, _, _) = VoltronicParser.DecodeStatusBits(0xC8, inV: 0, outV: 120);
        Assert.Equal(UpsOperationalState.BatteryLow, s3);

        var (s4, _, _) = VoltronicParser.DecodeStatusBits(0x28, inV: 100, outV: 120);
        Assert.Equal(UpsOperationalState.ChargingAvr, s4);

        // Fault overrides everything (0x10)
        var (s5, _, _) = VoltronicParser.DecodeStatusBits(0xF8, inV: 0, outV: 120);
        Assert.Equal(UpsOperationalState.Fault, s5);
    }

    [Fact]
    public void Voltronic_SelfTestCeiling_SurvivesStuckBit2AndClearsAt15Seconds()
    {
        var parser = new VoltronicParser();
        var t0 = new DateTimeOffset(2026, 10, 30, 10, 0, 0, TimeSpan.Zero);

        byte[] frameTesting = (byte[])GoldenFrame.Clone();
        frameTesting[23] = 0x0C; // Bit 2 = 1 (test active)

        byte[] frameIdle = (byte[])GoldenFrame.Clone();
        frameIdle[23] = 0x08; // Bit 2 = 0

        // Rising edge at t = 0
        Assert.True(parser.Parse(frameTesting, now: t0)!.IsTestInProgress);
        // Active at t = 14s
        Assert.True(parser.Parse(frameTesting, now: t0.AddSeconds(14))!.IsTestInProgress);
        // Expired at t = 15s
        Assert.False(parser.Parse(frameTesting, now: t0.AddSeconds(15))!.IsTestInProgress);
        // Stuck at t = 30s -> remains false!
        Assert.False(parser.Parse(frameTesting, now: t0.AddSeconds(30))!.IsTestInProgress);

        // Bit 2 clears at t = 35s
        Assert.False(parser.Parse(frameIdle, now: t0.AddSeconds(35))!.IsTestInProgress);

        // New test starts at t = 40s -> rising edge successfully arms
        Assert.True(parser.Parse(frameTesting, now: t0.AddSeconds(40))!.IsTestInProgress);
    }

    // =========================================================================
    // SECTION 3: ShutdownCoordinator Edge Cases & Debounce Stress Tests
    // =========================================================================

    [Fact]
    public void Coordinator_RepeatedSingleCycleGlitches_NeverTriggersShutdown()
    {
        var shutdown = new FakeShutdownPort();
        var notifications = new FakeNotificationPort();
        var coordinator = new ShutdownCoordinator(shutdown, notifications) { DryRun = false };

        // 10 cycles of alternating 1-cycle glitch and restoration
        for (int i = 0; i < 10; i++)
        {
            // 1 cycle of outage glitch with critical low battery and 1 min runtime
            coordinator.Evaluate(onBattery: true, lowBattery: true, runtimeMinutes: 1, isTestInProgress: false);
            Assert.Equal(1, coordinator.OutageCycleCount);
            Assert.Equal(0, shutdown.InitiateShutdownCalls);

            // Mains restored immediately
            coordinator.Evaluate(onBattery: false, lowBattery: false, runtimeMinutes: 25, isTestInProgress: false);
            Assert.Equal(0, coordinator.OutageCycleCount);
            Assert.Equal(0, shutdown.InitiateShutdownCalls);
        }

        Assert.Equal(0, shutdown.InitiateShutdownCalls);
    }

    [Fact]
    public void Coordinator_OscillatingRuntimeAround5Minutes_NeverTriggersShutdownUntilSustained()
    {
        var shutdown = new FakeShutdownPort();
        var notifications = new FakeNotificationPort();
        var coordinator = new ShutdownCoordinator(shutdown, notifications) { DryRun = false, RuntimeThresholdMinutes = 5 };

        // Confirmed outage: 2 cycles of on-battery
        coordinator.Evaluate(onBattery: true, lowBattery: false, runtimeMinutes: 10, isTestInProgress: false);
        coordinator.Evaluate(onBattery: true, lowBattery: false, runtimeMinutes: 10, isTestInProgress: false);
        Assert.Equal(2, coordinator.OutageCycleCount);

        // Oscillate runtime: 5, 6, 5, 6, 5, 6 (1 cycle below/at threshold, then 1 cycle above)
        for (int i = 0; i < 5; i++)
        {
            coordinator.Evaluate(onBattery: true, lowBattery: false, runtimeMinutes: 5, isTestInProgress: false);
            Assert.Equal(1, coordinator.LowRuntimeCycleCount);
            Assert.Equal(0, shutdown.InitiateShutdownCalls);

            coordinator.Evaluate(onBattery: true, lowBattery: false, runtimeMinutes: 6, isTestInProgress: false);
            Assert.Equal(0, coordinator.LowRuntimeCycleCount); // Debounce counter reset!
            Assert.Equal(0, shutdown.InitiateShutdownCalls);
        }

        // Now sustain low runtime for 2 consecutive cycles
        coordinator.Evaluate(onBattery: true, lowBattery: false, runtimeMinutes: 4, isTestInProgress: false);
        Assert.Equal(1, coordinator.LowRuntimeCycleCount);
        Assert.Equal(0, shutdown.InitiateShutdownCalls);

        coordinator.Evaluate(onBattery: true, lowBattery: false, runtimeMinutes: 4, isTestInProgress: false);
        Assert.Equal(2, coordinator.LowRuntimeCycleCount);
        Assert.Equal(1, shutdown.InitiateShutdownCalls); // Triggered!
    }

    [Fact]
    public void Coordinator_OutageDuringActiveSelfTest_EnforcesImmunityThenTriggersAfter15s()
    {
        var shutdown = new FakeShutdownPort();
        var notifications = new FakeNotificationPort();
        var coordinator = new ShutdownCoordinator(shutdown, notifications) { DryRun = false };

        var t0 = new DateTimeOffset(2026, 10, 30, 14, 0, 0, TimeSpan.Zero);

        // Self-test starts at t = 0 on mains
        coordinator.Evaluate(onBattery: false, lowBattery: false, runtimeMinutes: 25, isTestInProgress: true, now: t0);

        // Outage occurs during self-test at t = 4s
        coordinator.Evaluate(onBattery: true, lowBattery: true, runtimeMinutes: 1, isTestInProgress: true, now: t0.AddSeconds(4));
        Assert.Equal(1, coordinator.OutageCycleCount);
        Assert.Equal(0, shutdown.InitiateShutdownCalls);

        // Outage confirmed at t = 6s, still under 15s self-test
        coordinator.Evaluate(onBattery: true, lowBattery: true, runtimeMinutes: 1, isTestInProgress: true, now: t0.AddSeconds(6));
        Assert.Equal(2, coordinator.OutageCycleCount);
        Assert.Equal(0, shutdown.InitiateShutdownCalls); // Immunity active!

        // At t = 14s, still within 15s immunity
        coordinator.Evaluate(onBattery: true, lowBattery: true, runtimeMinutes: 1, isTestInProgress: true, now: t0.AddSeconds(14));
        Assert.Equal(0, shutdown.InitiateShutdownCalls);

        // At t = 15.5s, immunity expires; outage and low battery persist -> triggers shutdown!
        coordinator.Evaluate(onBattery: true, lowBattery: true, runtimeMinutes: 1, isTestInProgress: true, now: t0.AddSeconds(15.5));
        Assert.Equal(1, shutdown.InitiateShutdownCalls);
        Assert.Equal("Critical low battery detected during outage", shutdown.LastReason);
    }

    [Fact]
    public void Coordinator_RestorationCancelsShutdown_AndRequiresFullDebounceForNextIncident()
    {
        var shutdown = new FakeShutdownPort();
        var notifications = new FakeNotificationPort();
        var coordinator = new ShutdownCoordinator(shutdown, notifications) { DryRun = false };

        // Incident 1: Confirmed outage + low battery
        coordinator.Evaluate(onBattery: true, lowBattery: false, runtimeMinutes: 10, isTestInProgress: false);
        coordinator.Evaluate(onBattery: true, lowBattery: true, runtimeMinutes: 1, isTestInProgress: false);
        Assert.Equal(1, shutdown.InitiateShutdownCalls);
        Assert.True(coordinator.IsShutdownInitiated);

        // Mains restores
        coordinator.Evaluate(onBattery: false, lowBattery: false, runtimeMinutes: 25, isTestInProgress: false);
        Assert.Equal(1, shutdown.AbortShutdownCalls);
        Assert.False(coordinator.IsShutdownInitiated);
        Assert.Equal(0, coordinator.OutageCycleCount);

        // Incident 2: Single cycle glitch does not trigger
        coordinator.Evaluate(onBattery: true, lowBattery: true, runtimeMinutes: 1, isTestInProgress: false);
        Assert.Equal(1, coordinator.OutageCycleCount);
        Assert.Equal(1, shutdown.InitiateShutdownCalls); // Still 1 from incident 1

        // Cycle 2: Confirmed outage -> triggers incident 2!
        coordinator.Evaluate(onBattery: true, lowBattery: true, runtimeMinutes: 1, isTestInProgress: false);
        Assert.Equal(2, coordinator.OutageCycleCount);
        Assert.Equal(2, shutdown.InitiateShutdownCalls);
    }

    [Fact]
    public void Coordinator_EvaluateTelemetry_DemonstratesUnreachableWeakBatteryWarning()
    {
        var shutdown = new FakeShutdownPort();
        var notifications = new FakeNotificationPort();
        var coordinator = new ShutdownCoordinator(shutdown, notifications);

        // Scenario 1: Telemetry with LineNormal state (utility power present)
        var telemetryLineNormal = new UpsTelemetry(
            InputVoltageAc: 120.0,
            FaultReferenceVoltageAc: 120.0,
            OutputVoltageAc: 120.0,
            LoadPercentage: 14,
            ActiveLoadWatts: 126,
            FrequencyHz: 60.0,
            BatteryVoltageDc: 20.0, // Low battery voltage!
            Cell1VoltageDc: 10.0,
            Cell2VoltageDc: 10.0,
            BatteryPercentage: 0,   // Low battery %!
            EstimatedRuntimeMinutes: 25,
            TemperatureCelsius: 25.0,
            State: UpsOperationalState.LineNormal,
            IsBeeperMuted: true,
            IsTestInProgress: false,
            BankSpec: new BatteryBankSpec(),
            Timestamp: DateTimeOffset.UtcNow);

        coordinator.Evaluate(telemetryLineNormal);

        // In Evaluate(UpsTelemetry):
        // onBattery = (State is OnBattery or BatteryLow) -> false
        // lowBattery = (State == BatteryLow) -> false (because State is LineNormal)
        // Therefore, line 74 `if (lowBattery)` is FALSE. No weak battery warning!
        Assert.DoesNotContain(notifications.Notifications, n => n.Title == "Weak battery warning");

        // Scenario 2: Telemetry with BatteryLow state
        var telemetryBatteryLow = telemetryLineNormal with { State = UpsOperationalState.BatteryLow };

        // Reset notifications
        notifications.Notifications.Clear();
        coordinator.Evaluate(telemetryBatteryLow);

        // In Evaluate(UpsTelemetry):
        // onBattery = (State is OnBattery or BatteryLow) -> true
        // lowBattery = (State == BatteryLow) -> true
        // Therefore, `if (!onBattery)` is FALSE.
        // It enters outage handling, NOT the mains suppression branch!
        Assert.DoesNotContain(notifications.Notifications, n => n.Title == "Weak battery warning");

        // CONCLUSION: Through Evaluate(UpsTelemetry), `!onBattery && lowBattery` is a logical contradiction.
        // The "Weak battery warning" branch on line 74 is unreachable when calling Evaluate(UpsTelemetry).
    }
}
