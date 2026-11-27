using VoltTray.Core.Battery;
using VoltTray.Core.Telemetry;
using VoltTray.Infrastructure.Protocol;
using VoltTray.Shared;

namespace VoltTray.Infrastructure.Hid;

/// <summary>
/// Driverless Win32 Overlapped USB HID port for Voltronic/Forza UPS (VID_0665 PID_5161).
/// Communicates using 9-byte reports (Byte 0: 0x00 Report ID, Bytes 1..8: chunk payload).
/// Supports rapid release in under 100 ms when paused to avoid port contention.
/// </summary>
public sealed class Win32HidUpsPort : IUpsDevicePort, IRawTelemetryPort
{
    private readonly object _stateLock = new();
    private readonly HidCommandExecutor _commandExecutor = new();
    private readonly HidDeviceSessionOpener _sessionOpener;
    private readonly AsyncRetryPolicy _retryPolicy;

    private NegotiatedUpsProtocol _negotiatedProtocol;
    private UpsTelemetry? _lastTelemetry;
    private IHidConnection? _activeConnection;
    private CancellationTokenSource? _connectionCts;
    private CancellationTokenSource? _connectCts;
    private string? _activeDevicePath;
    private long _connectionGeneration;
    private bool _isDisposed;

    public Win32HidUpsPort(
        string? preferredDevicePath = null,
        bool allowAutoDiscovery = true,
        IBatteryAutonomyEstimator? estimator = null,
        IHidDeviceScanner? deviceScanner = null,
        UpsModelProfile? modelProfile = null)
        : this(preferredDevicePath, allowAutoDiscovery, estimator, deviceScanner, new Win32HidTransport(), negotiateOnConnect: true, retryPolicy: null, modelProfile: modelProfile)
    {
    }

    internal Win32HidUpsPort(
        string? preferredDevicePath,
        bool allowAutoDiscovery,
        IBatteryAutonomyEstimator? estimator,
        IHidDeviceScanner? deviceScanner,
        IHidTransport transport,
        bool negotiateOnConnect = false,
        AsyncRetryPolicy? retryPolicy = null,
        UpsModelProfile? modelProfile = null)
    {
        ArgumentNullException.ThrowIfNull(transport);

        IBatteryAutonomyEstimator resolvedEstimator = estimator ?? new PeukertEstimator();
        IHidDeviceScanner resolvedScanner = deviceScanner ?? new HidDeviceScanner();

        _sessionOpener = new HidDeviceSessionOpener(
            preferredDevicePath,
            allowAutoDiscovery,
            resolvedScanner,
            transport,
            resolvedEstimator,
            negotiateOnConnect,
            modelProfile);
        _retryPolicy = retryPolicy ?? new AsyncRetryPolicy();
        _negotiatedProtocol = _sessionOpener.CreateDefaultNegotiatedProtocol();
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
        return ConnectOnce(customPath, CancellationToken.None).IsSuccess;
    }

    public async Task<Result<string>> ConnectAsync(
        string? customPath = null,
        CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? connectAttemptCts;
        lock (_stateLock)
        {
            if (TryGetShortCircuitResultUnsafe(cancellationToken) is Result<string> shortCircuit)
            {
                return shortCircuit;
            }

            connectAttemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _connectCts = connectAttemptCts;
        }

        try
        {
            return await _retryPolicy
                .ExecuteAsync(
                    attemptToken => Task.FromResult(ConnectOnce(customPath, attemptToken)),
                    connectAttemptCts.Token)
                .ConfigureAwait(false);
        }
        finally
        {
            lock (_stateLock)
            {
                if (ReferenceEquals(_connectCts, connectAttemptCts))
                {
                    _connectCts = null;
                }
            }

            connectAttemptCts.Dispose();
        }
    }

    public void Disconnect()
    {
        IHidConnection? connectionToDispose;
        CancellationTokenSource? ctsToCancel;
        CancellationTokenSource? connectCtsToCancel;

        lock (_stateLock)
        {
            _connectionGeneration++;
            connectionToDispose = _activeConnection;
            _activeConnection = null;
            ctsToCancel = _connectionCts;
            _connectionCts = null;
            connectCtsToCancel = _connectCts;
            _connectCts = null;
            _activeDevicePath = null;
            _negotiatedProtocol.Strategy.ResetSelfTest();
        }

        HidDeviceSessionOpener.CancelSafely(connectCtsToCancel);
        HidDeviceSessionOpener.CancelSafely(ctsToCancel, dispose: true);

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
        RawUpsTelemetrySample? sample = await ReadRawTelemetryAsync(now, cancellationToken).ConfigureAwait(false);
        return sample?.Telemetry;
    }

    public async Task<RawUpsTelemetrySample?> ReadRawTelemetryAsync(
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

            return new RawUpsTelemetrySample(rawBytes, parsed);
        }
    }

    public Task<bool> ToggleBeeperAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteSerializedCommandAsync(
            VoltronicProtocolConstants.BeeperToggleCommand,
            cancellationToken,
            fallbackResult: false,
            executeAfterWrite: (connection, linkedToken, _) =>
                HidReportFramer.ReadControlAcknowledgement(connection, linkedToken));
    }

    public Task<bool> TriggerSelfTestAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteSerializedCommandAsync(
            VoltronicProtocolConstants.SelfTestCommand,
            cancellationToken,
            fallbackResult: false,
            executeAfterWrite: (connection, linkedToken, _) =>
                HidReportFramer.ReadControlAcknowledgement(connection, linkedToken));
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

    private Result<string> ConnectOnce(string? customPath, CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            if (TryGetShortCircuitResultUnsafe(cancellationToken) is Result<string> shortCircuit)
            {
                return shortCircuit;
            }
        }

        Result<(string DevicePath, IHidConnection Connection, NegotiatedUpsProtocol Protocol)> openResult =
            _sessionOpener.OpenSession(customPath);

        if (openResult.IsFailure)
        {
            return Result<string>.Failure(openResult.Error!);
        }

        var (targetPath, openedConnection, negotiatedProtocol) = openResult.Value;

        lock (_stateLock)
        {
            if (TryGetShortCircuitResultUnsafe(cancellationToken) is Result<string> shortCircuit)
            {
                openedConnection.Dispose();
                return shortCircuit;
            }

            _connectionGeneration++;
            _activeConnection = openedConnection;
            _connectionCts = new CancellationTokenSource();
            _activeDevicePath = targetPath;
            _negotiatedProtocol = negotiatedProtocol;
        }

        return Result<string>.Success(targetPath);
    }

    private Result<string>? TryGetShortCircuitResultUnsafe(CancellationToken cancellationToken)
    {
        return HidDeviceSessionOpener.CheckShortCircuit(
            _isDisposed,
            IsConnectedUnsafe(),
            _activeDevicePath,
            cancellationToken);
    }

    private bool IsConnectedUnsafe()
    {
        return _activeConnection != null && _activeConnection.IsOpen;
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
