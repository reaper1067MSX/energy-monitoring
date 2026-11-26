using VoltTray.Core.Telemetry;
using VoltTray.Core.Diagnostics;
using System.Text.Json;
using VoltTray.Infrastructure.Diagnostics;
using VoltTray.Infrastructure.Protocol;
using VoltTray.Tests.ObjectMothers;

namespace VoltTray.Tests;

public class InProcessTelemetryProbeTests
{
    private const int HealthyPollCycles = 500;
    private const int ExpectedRingCapacity = 256;
    private const int ExpectedDumpFrameCount = 16;

    [Fact]
    public void HealthyPolling_500Cycles_RetainsNewest256InMemoryAndWritesZeroFiles()
    {
        string tempDiagnosticsDir = CreateTempDiagnosticsDirectory();
        try
        {
            ITelemetryDiagnosticSink probe = new InProcessTelemetryProbe(tempDiagnosticsDir);
            var parser = new VoltronicParser();
            var baseTime = new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

            for (int cycle = 0; cycle < HealthyPollCycles; cycle++)
            {
                UpsTelemetry? parsed = parser.Parse(VoltronicFrameMother.Nt2401ProLiveFrame, now: baseTime.AddSeconds(cycle));
                probe.Record(VoltronicFrameMother.Nt2401ProLiveFrame, parsed, uiSummary: $"cycle-{cycle}");
            }

            Assert.Equal(ExpectedRingCapacity, probe.Count);
            Assert.False(Directory.Exists(tempDiagnosticsDir));

            string? dumpPath = probe.DumpRecent(ExpectedDumpFrameCount);
            Assert.NotNull(dumpPath);
            Assert.True(File.Exists(dumpPath));

            string[] lines = File.ReadAllLines(dumpPath);
            Assert.Equal(ExpectedDumpFrameCount, lines.Length);

            using JsonDocument oldestDumped = JsonDocument.Parse(lines[0]);
            using JsonDocument newestDumped = JsonDocument.Parse(lines[^1]);
            Assert.Equal("cycle-484", oldestDumped.RootElement.GetProperty("UiSummary").GetString());
            Assert.Equal("cycle-499", newestDumped.RootElement.GetProperty("UiSummary").GetString());
        }
        finally
        {
            DeleteTempDirectory(tempDiagnosticsDir);
        }
    }

    [Fact]
    public void MalformedFraming_AndPhysicsViolations_QuarantinedAndHoldLastKnownGoodUpTo3Cycles()
    {
        string tempDiagnosticsDir = CreateTempDiagnosticsDirectory();
        try
        {
            var probe = new InProcessTelemetryProbe(tempDiagnosticsDir);
            var parser = new VoltronicParser();
            var t0 = new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

            UpsTelemetry? healthy = parser.Parse(VoltronicFrameMother.Nt2401ProLiveFrame, now: t0);
            Assert.NotNull(healthy);

            TelemetryGuardOutcome healthyOutcome = probe.EvaluateAndRecord(
                VoltronicFrameMother.Nt2401ProLiveFrame,
                healthy,
                MetricProvenance.Unavailable,
                now: t0);

            Assert.False(healthyOutcome.IsQuarantined);
            Assert.False(healthyOutcome.UsedLastKnownGood);
            Assert.Null(healthyOutcome.Anomaly);
            Assert.Null(healthyOutcome.DiagnosticDumpPath);
            Assert.Same(healthy, healthyOutcome.EffectiveTelemetry);

            // Cycle 1 anomaly: Missing CR terminator (FramingError) -> holds LastKnownGood
            byte[] missingCrFrame = VoltronicFrameMother.CreateCorruptedMissingCr();
            TelemetryGuardOutcome cycle1 = probe.EvaluateAndRecord(
                missingCrFrame,
                healthy,
                MetricProvenance.Unavailable,
                now: t0.AddSeconds(1));

            Assert.True(cycle1.IsQuarantined);
            Assert.True(cycle1.UsedLastKnownGood);
            Assert.Equal(TelemetryAnomalyKind.FramingError, cycle1.Anomaly);
            Assert.Same(healthy, cycle1.EffectiveTelemetry);
            Assert.NotNull(cycle1.DiagnosticDumpPath);

            // Cycle 2 anomaly: Malformed 0x28 escape pair before CR (FramingError) -> holds LastKnownGood
            byte[] invalidEscapeFrame = [0x23, 0x72, 0x00, 0x28, 0x99, 0x0D];
            TelemetryGuardOutcome cycle2 = probe.EvaluateAndRecord(
                invalidEscapeFrame,
                healthy,
                MetricProvenance.Unavailable,
                now: t0.AddSeconds(2));

            Assert.True(cycle2.IsQuarantined);
            Assert.True(cycle2.UsedLastKnownGood);
            Assert.Equal(TelemetryAnomalyKind.FramingError, cycle2.Anomaly);
            Assert.Same(healthy, cycle2.EffectiveTelemetry);

            // Cycle 3 anomaly: Physics violation (4.27 Hz legacy frequency) -> holds LastKnownGood (3rd hold cycle)
            UpsTelemetry legacy427Hz = healthy with { FrequencyHz = 4.27 };
            TelemetryGuardOutcome cycle3 = probe.EvaluateAndRecord(
                VoltronicFrameMother.Nt2401ProLiveFrame,
                legacy427Hz,
                MetricProvenance.Unavailable,
                now: t0.AddSeconds(3));

            Assert.True(cycle3.IsQuarantined);
            Assert.True(cycle3.UsedLastKnownGood);
            Assert.Equal(TelemetryAnomalyKind.PhysicsImplausible, cycle3.Anomaly);
            Assert.Same(healthy, cycle3.EffectiveTelemetry);

            // Cycle 4 anomaly: InV = 0V while UtilityFail == 0 & BatteryVoltageDc unchanged -> exceeds 3-cycle hold -> null
            UpsTelemetry zeroInputWhileMainsNormal = healthy with { InputVoltageAc = 0.0 };
            TelemetryGuardOutcome cycle4 = probe.EvaluateAndRecord(
                VoltronicFrameMother.Nt2401ProLiveFrame,
                zeroInputWhileMainsNormal,
                MetricProvenance.Unavailable,
                now: t0.AddSeconds(4));

            Assert.True(cycle4.IsQuarantined);
            Assert.False(cycle4.UsedLastKnownGood);
            Assert.Equal(TelemetryAnomalyKind.PhysicsImplausible, cycle4.Anomaly);
            Assert.Null(cycle4.EffectiveTelemetry);

            // Healthy reading restores baseline; verify > 6.0V DC spike without state change is quarantined
            TelemetryGuardOutcome recovered = probe.EvaluateAndRecord(
                VoltronicFrameMother.Nt2401ProLiveFrame,
                healthy,
                MetricProvenance.Unavailable,
                now: t0.AddSeconds(5));
            Assert.False(recovered.IsQuarantined);

            UpsTelemetry dcSpikeSameState = healthy with { BatteryVoltageDc = healthy.BatteryVoltageDc - 7.5 };
            TelemetryGuardOutcome spikeOutcome = probe.EvaluateAndRecord(
                VoltronicFrameMother.Nt2401ProLiveFrame,
                dcSpikeSameState,
                MetricProvenance.Unavailable,
                now: t0.AddSeconds(6));

            Assert.True(spikeOutcome.IsQuarantined);
            Assert.True(spikeOutcome.UsedLastKnownGood);
            Assert.Equal(TelemetryAnomalyKind.PhysicsImplausible, spikeOutcome.Anomaly);
            Assert.Same(healthy, spikeOutcome.EffectiveTelemetry);
        }
        finally
        {
            DeleteTempDirectory(tempDiagnosticsDir);
        }
    }

    [Fact]
    public void QuarantinedAnomaly_After20Frames_Exports16FrameJsonlPreservingNullBytesAndNullUnavailableTemp()
    {
        string tempDiagnosticsDir = CreateTempDiagnosticsDirectory();
        try
        {
            var probe = new InProcessTelemetryProbe(tempDiagnosticsDir);
            var parser = new VoltronicParser();
            var t0 = new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

            for (int index = 0; index < 20; index++)
            {
                UpsTelemetry? parsed = parser.Parse(VoltronicFrameMother.Nt2401ProLiveFrame, now: t0.AddSeconds(index));
                TelemetryGuardOutcome outcome = probe.EvaluateAndRecord(
                    VoltronicFrameMother.Nt2401ProLiveFrame,
                    parsed,
                    MetricProvenance.Unavailable,
                    uiSummary: $"healthy-{index}",
                    now: t0.AddSeconds(index));
                Assert.False(outcome.IsQuarantined);
            }

            UpsTelemetry? baseline = parser.Parse(VoltronicFrameMother.Nt2401ProLiveFrame, now: t0.AddSeconds(20));
            Assert.NotNull(baseline);
            UpsTelemetry implausibleFreq = baseline with { FrequencyHz = 4.27 };

            TelemetryGuardOutcome anomalyOutcome = probe.EvaluateAndRecord(
                VoltronicFrameMother.Nt2401ProLiveFrame,
                implausibleFreq,
                MetricProvenance.Unavailable,
                uiSummary: "quarantined-21",
                now: t0.AddSeconds(20));

            Assert.True(anomalyOutcome.IsQuarantined);
            Assert.NotNull(anomalyOutcome.DiagnosticDumpPath);
            Assert.True(File.Exists(anomalyOutcome.DiagnosticDumpPath));

            string[] lines = File.ReadAllLines(anomalyOutcome.DiagnosticDumpPath);
            Assert.Equal(ExpectedDumpFrameCount, lines.Length);

            string expectedRawHex = Convert.ToHexString(VoltronicFrameMother.Nt2401ProLiveFrame);
            Assert.Contains("00", expectedRawHex);

            foreach (string line in lines)
            {
                using JsonDocument doc = JsonDocument.Parse(line);
                JsonElement root = doc.RootElement;
                Assert.Equal(expectedRawHex, root.GetProperty("RawHex").GetString());
                Assert.Equal(JsonValueKind.Null, root.GetProperty("TemperatureCelsius").ValueKind);
            }

            using JsonDocument lastEntry = JsonDocument.Parse(lines[^1]);
            Assert.Equal(nameof(TelemetryAnomalyKind.PhysicsImplausible), lastEntry.RootElement.GetProperty("Anomaly").GetString());
            Assert.Equal("quarantined-21", lastEntry.RootElement.GetProperty("UiSummary").GetString());
        }
        finally
        {
            DeleteTempDirectory(tempDiagnosticsDir);
        }
    }

    [Fact]
    public void ProvenanceUiDrift_WhenUnavailableTemperatureCoerced_QuarantinesAndExportsDump()
    {
        string tempDiagnosticsDir = CreateTempDiagnosticsDirectory();
        try
        {
            var probe = new InProcessTelemetryProbe(tempDiagnosticsDir);
            var parser = new VoltronicParser();
            var t0 = new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

            UpsTelemetry? baseline = parser.Parse(VoltronicFrameMother.Nt2401ProLiveFrame, now: t0);
            Assert.NotNull(baseline);
            Assert.Null(baseline.TemperatureCelsius);

            TelemetryGuardOutcome driftFromRenderedValue = probe.EvaluateAndRecord(
                VoltronicFrameMother.Nt2401ProLiveFrame,
                baseline,
                MetricProvenance.Unavailable,
                uiSummary: "rendered-25C-bug",
                renderedTemperatureCelsius: 25.0,
                now: t0);

            Assert.True(driftFromRenderedValue.IsQuarantined);
            Assert.Equal(TelemetryAnomalyKind.UiDrift, driftFromRenderedValue.Anomaly);
            Assert.NotNull(driftFromRenderedValue.DiagnosticDumpPath);
            Assert.True(File.Exists(driftFromRenderedValue.DiagnosticDumpPath));

            UpsTelemetry coercedDomainTemp = baseline with { TemperatureCelsius = 25.0 };
            TelemetryGuardOutcome driftFromDomainValue = probe.EvaluateAndRecord(
                VoltronicFrameMother.Nt2401ProLiveFrame,
                coercedDomainTemp,
                MetricProvenance.Unavailable,
                now: t0.AddSeconds(1));

            Assert.True(driftFromDomainValue.IsQuarantined);
            Assert.Equal(TelemetryAnomalyKind.UiDrift, driftFromDomainValue.Anomaly);
        }
        finally
        {
            DeleteTempDirectory(tempDiagnosticsDir);
        }
    }

    private static string CreateTempDiagnosticsDirectory()
    {
        return Path.Combine(Path.GetTempPath(), $"volttray-diag-test-{Guid.NewGuid():N}");
    }

    private static void DeleteTempDirectory(string directoryPath)
    {
        if (Directory.Exists(directoryPath))
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }
}
