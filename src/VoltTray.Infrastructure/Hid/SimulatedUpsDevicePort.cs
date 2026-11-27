using VoltTray.Core.Telemetry;
using VoltTray.Infrastructure.Protocol;
using VoltTray.Shared;

namespace VoltTray.Infrastructure.Hid;

/// <summary>
/// In-memory simulated UPS device port implementing Voltronic Protocol 'T' (QS-Hex).
/// Provides realistic telemetry snapshots with micro-jitter for local UI testing without physical hardware.
/// </summary>
public sealed class SimulatedUpsDevicePort : IUpsDevicePort, IRawTelemetryPort
{
    private readonly Random _random = new(HidConstants.DefaultRandomSeed);
    private readonly object _lock = new();
    private readonly VoltronicParser _parser = new();
    private readonly UpsCapabilities? _modelCapabilities;
    private UpsTelemetry? _lastTelemetry;
    private bool _isConnected = true;
    private bool _beeperActive = true;
    private bool _selfTestActive;
    private DateTime _selfTestExpires = DateTime.MinValue;

    public SimulatedUpsDevicePort(UpsModelProfile? modelProfile = null)
    {
        _modelCapabilities = modelProfile?.ToCapabilities();
    }

    public bool IsConnected
    {
        get
        {
            lock (_lock)
            {
                return _isConnected;
            }
        }
    }

    public string? ActiveDevicePath => HidConstants.SimulatedDevicePath;

    public bool TryConnect(string? customPath = null)
    {
        lock (_lock)
        {
            _isConnected = true;
            return true;
        }
    }

    public Task<Result<string>> ConnectAsync(
        string? customPath = null,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Result<string>.Failure(new DomainError(
                SharedErrorConstants.TransportTimeoutCode,
                "Simulated UPS connection cancelled.",
                ErrorCategory.TransportTimeout,
                IsTransient: false)));
        }

        TryConnect(customPath);
        return Task.FromResult(Result<string>.Success(ActiveDevicePath!));
    }

    public void Disconnect()
    {
        lock (_lock)
        {
            _isConnected = false;
            _parser.ResetSelfTest();
        }
    }

    public Task<UpsTelemetry?> ReadTelemetryAsync(
        DateTimeOffset? now = null,
        CancellationToken cancellationToken = default)
    {
        RawUpsTelemetrySample? sample = CreateTelemetrySample(now);
        return Task.FromResult(sample?.Telemetry);
    }

    public Task<RawUpsTelemetrySample?> ReadRawTelemetryAsync(
        DateTimeOffset? now = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CreateTelemetrySample(now));
    }

    private RawUpsTelemetrySample? CreateTelemetrySample(DateTimeOffset? now)
    {
        lock (_lock)
        {
            if (!_isConnected)
            {
                return null;
            }

            if (_selfTestActive && DateTime.UtcNow >= _selfTestExpires)
            {
                _selfTestActive = false;
            }

            int loadPercentage =
                VoltronicProtocolConstants.NominalLoadPercentage +
                _random.Next(HidConstants.MinimumLoadJitterOffset, HidConstants.MaximumLoadJitterExclusiveOffset);

            byte statusByte1 = VoltronicProtocolConstants.StatusLineNormal;
            if (_beeperActive)
            {
                statusByte1 |= VoltronicProtocolConstants.StatusBeeperActiveMask;
            }

            if (_selfTestActive)
            {
                statusByte1 |= VoltronicProtocolConstants.StatusSelfTestActiveMask;
            }

            var specification = new VoltronicFrameSpecification
            {
                StatusByte1 = statusByte1,
                LoadPercentage = (byte)loadPercentage
            };

            byte[] frameBytes = VoltronicBinaryFrameBuilder.Build(specification);
            UpsTelemetry? parsed = _modelCapabilities is not null
                ? _parser.Parse(frameBytes, _modelCapabilities, _lastTelemetry, now)
                : _parser.Parse(frameBytes, _lastTelemetry, now);
            if (parsed != null)
            {
                _lastTelemetry = parsed;
            }

            return new RawUpsTelemetrySample(frameBytes, parsed);
        }
    }

    public Task<bool> ToggleBeeperAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!_isConnected)
            {
                return Task.FromResult(false);
            }

            _beeperActive = !_beeperActive;
            return Task.FromResult(true);
        }
    }

    public Task<bool> TriggerSelfTestAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!_isConnected)
            {
                return Task.FromResult(false);
            }

            _selfTestActive = true;
            _selfTestExpires = DateTime.UtcNow.AddSeconds(ElectricalConstants.MaxSelfTestDurationSeconds);
            return Task.FromResult(true);
        }
    }

    public void Dispose()
    {
        Disconnect();
    }
}
