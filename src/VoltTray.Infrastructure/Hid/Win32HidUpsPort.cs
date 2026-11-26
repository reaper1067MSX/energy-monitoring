using System.Diagnostics;
using VoltTray.Core.Models;
using VoltTray.Core.Ports;
using VoltTray.Core.Services;
using VoltTray.Infrastructure.Protocol;

namespace VoltTray.Infrastructure.Hid;

/// <summary>
/// Driverless Win32 Overlapped USB HID port for Voltronic/Forza UPS (VID_0665 PID_5161).
/// Communicates using 9-byte reports (Byte 0: 0x00 Report ID, Bytes 1..8: chunk payload).
/// Supports rapid release in under 100 ms when paused to avoid port contention.
/// </summary>
public sealed class Win32HidUpsPort : IUpsDevicePort
{
    private readonly object _stateLock = new();
    private readonly HidCommandExecutor _commandExecutor = new();
    private readonly string? _preferredDevicePath;
    private readonly bool _allowAutoDiscovery;
    private readonly IHidDeviceScanner _deviceScanner;
    private readonly IHidTransport _transport;
    private readonly IBatteryAutonomyEstimator _estimator;
    private readonly ProtocolNegotiator _protocolNegotiator = new();
    private readonly bool _negotiateOnConnect;

    private NegotiatedUpsProtocol _negotiatedProtocol;
    private UpsTelemetry? _lastTelemetry;
    private IHidConnection? _activeConnection;
    private CancellationTokenSource? _connectionCts;
    private string? _activeDevicePath;
    private long _connectionGeneration;
    private bool _isDisposed;

    public Win32HidUpsPort(
        string? preferredDevicePath = null,
        bool allowAutoDiscovery = true,
        IBatteryAutonomyEstimator? estimator = null,
        IHidDeviceScanner? deviceScanner = null)
        : this(preferredDevicePath, allowAutoDiscovery, estimator, deviceScanner, new Win32HidTransport(), negotiateOnConnect: true)
    {
    }

    internal Win32HidUpsPort(
        string? preferredDevicePath,
        bool allowAutoDiscovery,
        IBatteryAutonomyEstimator? estimator,
        IHidDeviceScanner? deviceScanner,
        IHidTransport transport,
        bool negotiateOnConnect = false)
    {
        _preferredDevicePath = preferredDevicePath;
        _allowAutoDiscovery = allowAutoDiscovery;
        _deviceScanner = deviceScanner ?? new HidDeviceScanner();
        _transport = transport;
        _estimator = estimator ?? new PeukertEstimator();
        _negotiateOnConnect = negotiateOnConnect;
        _negotiatedProtocol = CreateDefaultNegotiatedProtocol();
    }

    public NegotiatedUpsProtocol NegotiatedProtocol
    {
        get
        {
            lock (_stateLock)
            {
                return _negotiatedProtocol;
            }
        }
    }

    public bool IsConnected
    {
        get
        {
            lock (_stateLock)
            {
                return IsConnectedUnsafe();
            }
        }
    }

    public string? ActiveDevicePath
    {
        get
        {
            lock (_stateLock)
            {
                return _activeDevicePath;
            }
        }
    }

    public bool TryConnect(string? customPath = null)
    {
        lock (_stateLock)
        {
            if (_isDisposed)
            {
                return false;
            }

            if (IsConnectedUnsafe())
            {
                return true;
            }
        }

        string? targetPath = ResolveTargetPath(customPath);
        if (string.IsNullOrEmpty(targetPath))
        {
            return false;
        }

        IHidConnection? openedConnection = _transport.TryOpen(targetPath);
        if (openedConnection == null)
        {
            return false;
        }

        openedConnection.DrainInputBuffer(HidConstants.DefaultDrainTimeoutMs);

        NegotiatedUpsProtocol negotiatedProtocol = _negotiateOnConnect
            ? _protocolNegotiator.Negotiate(
                command => HidCommandExecutor.QuerySynchronously(openedConnection, command),
                _estimator)
            : CreateDefaultNegotiatedProtocol();

        lock (_stateLock)
        {
            if (_isDisposed)
            {
                openedConnection.Dispose();
                return false;
            }

            if (IsConnectedUnsafe())
            {
                openedConnection.Dispose();
                return true;
            }

            _connectionGeneration++;
            _activeConnection = openedConnection;
            _connectionCts = new CancellationTokenSource();
            _activeDevicePath = targetPath;
            _negotiatedProtocol = negotiatedProtocol;
        }

        return true;
    }

    public void Disconnect()
    {
        IHidConnection? connectionToDispose;
        CancellationTokenSource? ctsToCancel;

        lock (_stateLock)
        {
            _connectionGeneration++;
            connectionToDispose = _activeConnection;
            _activeConnection = null;
            ctsToCancel = _connectionCts;
            _connectionCts = null;
            _activeDevicePath = null;
            _negotiatedProtocol.Strategy.ResetSelfTest();
        }

        if (ctsToCancel != null)
        {
            try
            {
                ctsToCancel.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            ctsToCancel.Dispose();
        }

        if (connectionToDispose != null)
        {
            connectionToDispose.CancelPendingIo();
            connectionToDispose.Dispose();
        }
    }

    public async Task<UpsTelemetry?> ReadTelemetryAsync(
        DateTimeOffset? now = null,
        CancellationToken cancellationToken = default)
    {
        string statusQueryCommand;
        lock (_stateLock)
        {
            statusQueryCommand = _negotiatedProtocol.Strategy.StatusQueryCommand;
        }

        var (rawBytes, capturedGeneration) = await QueryRawWithGenerationAsync(
            statusQueryCommand,
            cancellationToken);

        if (rawBytes is null or { Length: 0 })
        {
            return null;
        }

        lock (_stateLock)
        {
            if (_connectionGeneration != capturedGeneration || !IsConnectedUnsafe())
            {
                return null;
            }

            DateTimeOffset timestamp = now ?? DateTimeOffset.UtcNow;
            UpsTelemetry? parsed = _negotiatedProtocol.Strategy.Parse(
                rawBytes,
                _negotiatedProtocol.Capabilities,
                _lastTelemetry,
                timestamp);

            if (parsed != null)
            {
                _lastTelemetry = parsed;
            }

            return parsed;
        }
    }

    public Task<bool> ToggleBeeperAsync(CancellationToken cancellationToken = default)
    {
        return SendControlCommandAsync(VoltronicProtocolConstants.BeeperToggleCommand, cancellationToken);
    }

    public Task<bool> TriggerSelfTestAsync(CancellationToken cancellationToken = default)
    {
        return SendControlCommandAsync(VoltronicProtocolConstants.SelfTestCommand, cancellationToken);
    }

    public async Task<byte[]?> QueryRawAsync(string command, CancellationToken cancellationToken = default)
    {
        var (rawBytes, _) = await QueryRawWithGenerationAsync(command, cancellationToken);
        return rawBytes;
    }

    public void Dispose()
    {
        lock (_stateLock)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
        }

        Disconnect();
        _commandExecutor.Dispose();
    }

    private bool IsConnectedUnsafe()
    {
        return _activeConnection != null && _activeConnection.IsOpen;
    }

    private string? ResolveTargetPath(string? customPath)
    {
        if (!string.IsNullOrEmpty(customPath))
        {
            return customPath;
        }

        if (!string.IsNullOrEmpty(_preferredDevicePath))
        {
            return _preferredDevicePath;
        }

        if (!_allowAutoDiscovery)
        {
            return null;
        }

        HidDeviceDescriptor? discoveredDevice = _deviceScanner
            .DiscoverDevices(includeAllHidDevices: false)
            .FirstOrDefault(descriptor => descriptor.IsSupportedVoltronicDevice);

        return discoveredDevice?.DevicePath;
    }

    private NegotiatedUpsProtocol CreateDefaultNegotiatedProtocol()
    {
        return new NegotiatedUpsProtocol(
            new VoltronicQsHexProtocol(VoltronicProtocolConstants.DialectT, _estimator),
            new UpsCapabilities(
                ModelName: null,
                NominalVoltageAc: null,
                NominalFrequencyHz: null,
                RatedWatts: null,
                Bank: new BatteryBankSpec(),
                DialectVerified: false));
    }

    private bool TrySnapshotActiveConnection(
        CancellationToken callerToken,
        out IHidConnection? connection,
        out CancellationTokenSource? linkedCts,
        out long generation)
    {
        lock (_stateLock)
        {
            if (_isDisposed || !IsConnectedUnsafe() || _connectionCts == null)
            {
                connection = null;
                linkedCts = null;
                generation = 0;
                return false;
            }

            connection = _activeConnection;
            generation = _connectionGeneration;
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(callerToken, _connectionCts.Token);
            return true;
        }
    }

    private Task<(byte[]? RawBytes, long Generation)> QueryRawWithGenerationAsync(
        string command,
        CancellationToken cancellationToken)
    {
        return ExecuteSerializedCommandAsync<(byte[]?, long)>(
            command,
            cancellationToken,
            fallbackResult: (null, 0L),
            executeAfterWrite: (connection, linkedToken, generation) =>
            {
                byte[]? response = HidReportFramer.ReadFramedResponse(connection, command, linkedToken);
                return (response, response != null ? generation : 0L);
            });
    }

    private Task<bool> SendControlCommandAsync(string command, CancellationToken cancellationToken)
    {
        return ExecuteSerializedCommandAsync(
            command,
            cancellationToken,
            fallbackResult: false,
            executeAfterWrite: (connection, linkedToken, _) =>
                HidReportFramer.ReadControlAcknowledgement(connection, linkedToken));
    }

    private async Task<TResult> ExecuteSerializedCommandAsync<TResult>(
        string command,
        CancellationToken cancellationToken,
        TResult fallbackResult,
        Func<IHidConnection, CancellationToken, long, TResult> executeAfterWrite)
    {
        if (cancellationToken.IsCancellationRequested || (!IsConnected && !TryConnect()))
        {
            return fallbackResult;
        }

        if (!TrySnapshotActiveConnection(
                cancellationToken,
                out IHidConnection? connection,
                out CancellationTokenSource? linkedCts,
                out long generation) ||
            connection == null ||
            linkedCts == null)
        {
            return fallbackResult;
        }

        try
        {
            return await _commandExecutor.ExecuteSerializedAsync(
                connection,
                linkedCts.Token,
                generation,
                command,
                fallbackResult,
                Disconnect,
                executeAfterWrite).ConfigureAwait(false);
        }
        finally
        {
            linkedCts.Dispose();
        }
    }
}
