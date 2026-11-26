using System.Globalization;
using System.Text;
using System.Text.Json;
using VoltTray.Core.Models;
using VoltTray.Core.Ports;

namespace VoltTray.Infrastructure.Diagnostics;

/// <summary>
/// Preallocated 256-slot circular in-memory flight recorder with Framing/Physics/UI-Drift
/// quarantine, 3-cycle LastKnownGood hold, and automatic Native AOT 16-frame .jsonl export on anomaly.
/// </summary>
public sealed class InProcessTelemetryProbe : ITelemetryDiagnosticSink
{
    private readonly object _syncLock = new();
    private readonly TelemetryFlightRecorderEntry?[] _entries =
        new TelemetryFlightRecorderEntry?[DiagnosticsConstants.RingBufferCapacity];
    private readonly string _diagnosticsDirectory;

    private int _nextWriteIndex;
    private int _count;
    private int _consecutiveAnomalyCount;
    private long _dumpSequence;
    private UpsTelemetry? _lastKnownGood;

    public InProcessTelemetryProbe(string? diagnosticsDirectory = null)
    {
        _diagnosticsDirectory = string.IsNullOrWhiteSpace(diagnosticsDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                DiagnosticsConstants.ApplicationFolderName,
                DiagnosticsConstants.DiagnosticsFolderName)
            : diagnosticsDirectory;
    }

    public int Count
    {
        get
        {
            lock (_syncLock)
            {
                return _count;
            }
        }
    }

    public void Record(
        ReadOnlySpan<byte> rawFrame,
        UpsTelemetry? domainTelemetry,
        TelemetryAnomalyKind? anomaly = null,
        string? uiSummary = null)
    {
        TelemetryFlightRecorderEntry entry = CreateEntry(
            DateTimeOffset.UtcNow,
            rawFrame,
            domainTelemetry,
            anomaly,
            uiSummary);

        lock (_syncLock)
        {
            AppendEntryUnsafe(entry);
        }
    }

    public TelemetryGuardOutcome EvaluateAndRecord(
        ReadOnlySpan<byte> rawFrame,
        UpsTelemetry? parsedTelemetry,
        MetricProvenance temperatureProvenance = MetricProvenance.Unavailable,
        string? uiSummary = null,
        double? renderedTemperatureCelsius = null,
        DateTimeOffset? now = null)
    {
        DateTimeOffset timestamp = now ?? DateTimeOffset.UtcNow;
        TelemetryAnomalyKind? anomaly = TelemetryPlausibilityGuard.EvaluateFraming(rawFrame);

        if (anomaly is null && parsedTelemetry is null)
        {
            anomaly = TelemetryAnomalyKind.FramingError;
        }

        List<TelemetryFlightRecorderEntry> snapshotToDump;
        UpsTelemetry? effectiveTelemetry;
        bool canHoldLastKnownGood;
        long dumpSequence;

        lock (_syncLock)
        {
            if (anomaly is null && parsedTelemetry != null)
            {
                anomaly = TelemetryPlausibilityGuard.EvaluatePhysics(parsedTelemetry, _lastKnownGood);
            }

            if (anomaly is null && parsedTelemetry != null)
            {
                anomaly = TelemetryPlausibilityGuard.EvaluateProvenanceDrift(
                    parsedTelemetry,
                    temperatureProvenance,
                    renderedTemperatureCelsius);
            }

            TelemetryFlightRecorderEntry entry = CreateEntry(timestamp, rawFrame, parsedTelemetry, anomaly, uiSummary);
            AppendEntryUnsafe(entry);

            if (anomaly is null && parsedTelemetry != null)
            {
                _lastKnownGood = parsedTelemetry;
                _consecutiveAnomalyCount = 0;

                return new TelemetryGuardOutcome(
                    EffectiveTelemetry: parsedTelemetry,
                    Anomaly: null,
                    IsQuarantined: false,
                    UsedLastKnownGood: false,
                    DiagnosticDumpPath: null);
            }

            _consecutiveAnomalyCount++;
            canHoldLastKnownGood =
                _consecutiveAnomalyCount <= DiagnosticsConstants.MaxLastKnownGoodHoldCycles &&
                _lastKnownGood != null;
            effectiveTelemetry = canHoldLastKnownGood ? _lastKnownGood : null;
            snapshotToDump = SnapshotRecentEntriesUnsafe(DiagnosticsConstants.DefaultDumpCount);
            dumpSequence = ++_dumpSequence;
        }

        string? dumpPath = WriteSnapshotFile(snapshotToDump, timestamp, dumpSequence);
        return new TelemetryGuardOutcome(
            EffectiveTelemetry: effectiveTelemetry,
            Anomaly: anomaly,
            IsQuarantined: true,
            UsedLastKnownGood: canHoldLastKnownGood,
            DiagnosticDumpPath: dumpPath);
    }

    public string? DumpRecent(int count = DiagnosticsConstants.DefaultDumpCount)
    {
        List<TelemetryFlightRecorderEntry> snapshot;
        long dumpSequence;

        lock (_syncLock)
        {
            if (_count == 0)
            {
                return null;
            }

            int clampedCount = Math.Clamp(count, 1, DiagnosticsConstants.RingBufferCapacity);
            snapshot = SnapshotRecentEntriesUnsafe(clampedCount);
            dumpSequence = ++_dumpSequence;
        }

        return WriteSnapshotFile(snapshot, DateTimeOffset.UtcNow, dumpSequence);
    }

    private void AppendEntryUnsafe(TelemetryFlightRecorderEntry entry)
    {
        _entries[_nextWriteIndex] = entry;
        _nextWriteIndex = (_nextWriteIndex + 1) % DiagnosticsConstants.RingBufferCapacity;
        if (_count < DiagnosticsConstants.RingBufferCapacity)
        {
            _count++;
        }
    }

    private List<TelemetryFlightRecorderEntry> SnapshotRecentEntriesUnsafe(int requestedCount)
    {
        int actualCount = Math.Min(requestedCount, _count);
        var entries = new List<TelemetryFlightRecorderEntry>(actualCount);
        int oldestIndex =
            (_nextWriteIndex - actualCount + DiagnosticsConstants.RingBufferCapacity) %
            DiagnosticsConstants.RingBufferCapacity;

        for (int offset = 0; offset < actualCount; offset++)
        {
            TelemetryFlightRecorderEntry? entry =
                _entries[(oldestIndex + offset) % DiagnosticsConstants.RingBufferCapacity];
            if (entry != null)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    private string? WriteSnapshotFile(
        List<TelemetryFlightRecorderEntry> entries,
        DateTimeOffset timestamp,
        long sequence)
    {
        if (entries.Count == 0)
        {
            return null;
        }

        Directory.CreateDirectory(_diagnosticsDirectory);
        string formattedTimestamp =
            timestamp.UtcDateTime.ToString(DiagnosticsConstants.DumpTimestampFormat, CultureInfo.InvariantCulture);
        string fileName = $"anomaly-{formattedTimestamp}-{sequence}.jsonl";
        string fullPath = Path.Combine(_diagnosticsDirectory, fileName);

        var builder = new StringBuilder();
        foreach (TelemetryFlightRecorderEntry entry in entries)
        {
            string jsonLine = JsonSerializer.Serialize(
                entry,
                TelemetryFlightRecorderJsonContext.Default.TelemetryFlightRecorderEntry);
            builder.AppendLine(jsonLine);
        }

        File.WriteAllText(fullPath, builder.ToString(), Encoding.UTF8);
        return fullPath;
    }

    private static TelemetryFlightRecorderEntry CreateEntry(
        DateTimeOffset timestamp,
        ReadOnlySpan<byte> rawFrame,
        UpsTelemetry? domainTelemetry,
        TelemetryAnomalyKind? anomaly,
        string? uiSummary)
    {
        return new TelemetryFlightRecorderEntry(
            TimestampUtc: timestamp.ToUniversalTime(),
            RawHex: Convert.ToHexString(rawFrame),
            InputVoltageAc: domainTelemetry?.InputVoltageAc,
            OutputVoltageAc: domainTelemetry?.OutputVoltageAc,
            FrequencyHz: domainTelemetry?.FrequencyHz,
            BatteryVoltageDc: domainTelemetry?.BatteryVoltageDc,
            TemperatureCelsius: domainTelemetry?.TemperatureCelsius,
            EstimatedRuntimeMinutes: domainTelemetry?.EstimatedRuntimeMinutes,
            State: domainTelemetry?.State.ToString(),
            Anomaly: anomaly?.ToString(),
            UiSummary: uiSummary);
    }
}
