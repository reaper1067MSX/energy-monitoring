using System.Diagnostics;
using VoltTray.Core.Models;
using VoltTray.Core.Ports;
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
    private readonly SemaphoreSlim _ioGate = new(1, 1);
    private readonly string? _preferredDevicePath;
    private readonly bool _allowAutoDiscovery;
    private readonly IHidDeviceScanner _deviceScanner;
    private readonly IHidTransport _transport;
    private readonly VoltronicParser _parser;

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
        : this(preferredDevicePath, allowAutoDiscovery, estimator, deviceScanner, new Win32HidTransport())
    {
    }

    internal Win32HidUpsPort(
        string? preferredDevicePath,
        bool allowAutoDiscovery,
        IBatteryAutonomyEstimator? estimator,
        IHidDeviceScanner? deviceScanner,
        IHidTransport transport)
    {
        _preferredDevicePath = preferredDevicePath;
        _allowAutoDiscovery = allowAutoDiscovery;
        _deviceScanner = deviceScanner ?? new HidDeviceScanner();
        _transport = transport;
        _parser = new VoltronicParser(estimator);
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
            _parser.ResetSelfTest();
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
        var (rawBytes, capturedGeneration) = await QueryRawWithGenerationAsync(
            VoltronicProtocolConstants.QueryStatusCommand,
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

            UpsTelemetry? parsed = _parser.Parse(rawBytes, _lastTelemetry, now);
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
        _ioGate.Dispose();
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
            .FirstOrDefault(descriptor =>
                descriptor.VendorId == HidConstants.VendorId &&
                descriptor.ProductId == HidConstants.ProductId);

        return discoveredDevice?.DevicePath;
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
            await _ioGate.WaitAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
        {
            linkedCts.Dispose();
            return fallbackResult;
        }

        try
        {
            return await Task.Run(() =>
            {
                if (linkedCts.IsCancellationRequested || !connection.IsOpen)
                {
                    return fallbackResult;
                }

                try
                {
                    connection.DrainInputBuffer(HidConstants.DefaultDrainTimeoutMs);
                    if (linkedCts.IsCancellationRequested || !connection.WriteCommand(command))
                    {
                        if (!linkedCts.IsCancellationRequested)
                        {
                            Disconnect();
                        }

                        return fallbackResult;
                    }

                    return executeAfterWrite(connection, linkedCts.Token, generation);
                }
                catch
                {
                    if (!linkedCts.IsCancellationRequested)
                    {
                        Disconnect();
                    }

                    return fallbackResult;
                }
            }, linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return fallbackResult;
        }
        finally
        {
            ReleaseIoGateSafely();
            linkedCts.Dispose();
        }
    }

    private void ReleaseIoGateSafely()
    {
        try
        {
            _ioGate.Release();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
