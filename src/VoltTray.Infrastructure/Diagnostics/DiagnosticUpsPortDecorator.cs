using VoltTray.Core.Models;
using VoltTray.Core.Ports;
using VoltTray.Infrastructure.Hid;
using VoltTray.Shared;

namespace VoltTray.Infrastructure.Diagnostics;

/// <summary>
/// Decorates an <see cref="IUpsDevicePort"/> to record raw HID frames (preserving 0x00 bytes)
/// and run Framing/Physics/UI-Drift plausibility guards before telemetry reaches callers.
/// </summary>
public sealed class DiagnosticUpsPortDecorator : IUpsDevicePort
{
    private readonly IUpsDevicePort _innerPort;
    private readonly InProcessTelemetryProbe _telemetryProbe;
    private readonly MetricProvenance _temperatureProvenance;

    private UpsTelemetry? _fallbackLastKnownGood;
    private int _fallbackConsecutiveAnomalies;

    public DiagnosticUpsPortDecorator(
        IUpsDevicePort innerPort,
        InProcessTelemetryProbe telemetryProbe,
        MetricProvenance temperatureProvenance = MetricProvenance.Unavailable)
    {
        _innerPort = innerPort ?? throw new ArgumentNullException(nameof(innerPort));
        _telemetryProbe = telemetryProbe ?? throw new ArgumentNullException(nameof(telemetryProbe));
        _temperatureProvenance = temperatureProvenance;
    }

    public bool IsConnected => _innerPort.IsConnected;

    public string? ActiveDevicePath => _innerPort.ActiveDevicePath;

    public bool TryConnect(string? customPath = null)
    {
        return _innerPort.TryConnect(customPath);
    }

    public Task<Result<string>> ConnectAsync(
        string? customPath = null,
        CancellationToken cancellationToken = default)
    {
        return _innerPort.ConnectAsync(customPath, cancellationToken);
    }

    public void Disconnect()
    {
        _innerPort.Disconnect();
    }

    public async Task<UpsTelemetry?> ReadTelemetryAsync(
        DateTimeOffset? now = null,
        CancellationToken cancellationToken = default)
    {
        if (_innerPort is IRawTelemetryPort rawTelemetryPort)
        {
            RawUpsTelemetrySample? sample = await rawTelemetryPort
                .ReadRawTelemetryAsync(now, cancellationToken)
                .ConfigureAwait(false);

            if (sample is null)
            {
                return null;
            }

            TelemetryGuardOutcome outcome = _telemetryProbe.EvaluateAndRecord(
                sample.RawFrame,
                sample.Telemetry,
                _temperatureProvenance,
                now: now);

            return outcome.EffectiveTelemetry;
        }

        UpsTelemetry? parsed = await _innerPort
            .ReadTelemetryAsync(now, cancellationToken)
            .ConfigureAwait(false);

        if (parsed is null)
        {
            return null;
        }

        TelemetryAnomalyKind? anomaly =
            TelemetryPlausibilityGuard.EvaluatePhysics(parsed, _fallbackLastKnownGood)
            ?? TelemetryPlausibilityGuard.EvaluateProvenanceDrift(parsed, _temperatureProvenance);

        if (anomaly is null)
        {
            _fallbackLastKnownGood = parsed;
            _fallbackConsecutiveAnomalies = 0;
            _telemetryProbe.Record(ReadOnlySpan<byte>.Empty, parsed);
            return parsed;
        }

        _fallbackConsecutiveAnomalies++;
        _telemetryProbe.Record(
            ReadOnlySpan<byte>.Empty,
            parsed,
            anomaly,
            "Quarantined in DiagnosticUpsPortDecorator");
        _telemetryProbe.DumpRecent();

        bool canHoldLastKnownGood =
            _fallbackConsecutiveAnomalies <= DiagnosticsConstants.MaxLastKnownGoodHoldCycles &&
            _fallbackLastKnownGood != null;

        return canHoldLastKnownGood ? _fallbackLastKnownGood : null;
    }

    public Task<bool> ToggleBeeperAsync(CancellationToken cancellationToken = default)
    {
        return _innerPort.ToggleBeeperAsync(cancellationToken);
    }

    public Task<bool> TriggerSelfTestAsync(CancellationToken cancellationToken = default)
    {
        return _innerPort.TriggerSelfTestAsync(cancellationToken);
    }

    public void Dispose()
    {
        _innerPort.Dispose();
    }
}
