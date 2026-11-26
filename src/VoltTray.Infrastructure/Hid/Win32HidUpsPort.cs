using System.Diagnostics;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
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
    public const string DefaultDevicePath = HidConstants.DefaultDevicePath;
    private const string HidInterfaceGuid = HidConstants.HidInterfaceGuid;

    private readonly object _lock = new();
    private readonly string _defaultDevicePath;
    private readonly bool _allowRegistryFallback;
    private readonly VoltronicParser _parser;
    private UpsTelemetry? _lastTelemetry;
    private SafeFileHandle? _handle;
    private bool _isDisposed;

    public Win32HidUpsPort(
        string? defaultDevicePath = null,
        bool allowRegistryFallback = true,
        IBatteryAutonomyEstimator? estimator = null)
    {
        _defaultDevicePath = defaultDevicePath ?? DefaultDevicePath;
        _allowRegistryFallback = allowRegistryFallback;
        _parser = new VoltronicParser(estimator);
    }

    public bool IsConnected
    {
        get
        {
            lock (_lock)
            {
                return _handle != null && !_handle.IsInvalid && !_handle.IsClosed;
            }
        }
    }

    public string? ActiveDevicePath { get; private set; }

    public bool TryConnect(string? customPath = null)
    {
        lock (_lock)
        {
            if (IsConnected)
            {
                return true;
            }

            DisconnectInternal();

            string targetPath = customPath ?? _defaultDevicePath;
            if (TryOpenHandle(targetPath))
            {
                ActiveDevicePath = targetPath;
                DrainStaleBufferInternal();
                return true;
            }

            if (customPath != null || !_allowRegistryFallback)
            {
                return false;
            }

            return TryConnectViaRegistryFallback();
        }
    }

    public void Disconnect()
    {
        lock (_lock)
        {
            DisconnectInternal();
        }
    }

    public async Task<UpsTelemetry?> ReadTelemetryAsync(
        DateTimeOffset? now = null,
        CancellationToken cancellationToken = default)
    {
        byte[]? raw = await QueryRawAsync(VoltronicProtocolConstants.QueryStatusCommand, cancellationToken);
        if (raw is null or { Length: 0 })
        {
            return null;
        }

        UpsTelemetry? parsed = _parser.Parse(raw, _lastTelemetry, now);
        if (parsed != null)
        {
            _lastTelemetry = parsed;
        }

        return parsed;
    }

    public Task<bool> ToggleBeeperAsync(CancellationToken cancellationToken = default)
    {
        return SendControlCommandAsync(VoltronicProtocolConstants.BeeperToggleCommand, cancellationToken);
    }

    public Task<bool> TriggerSelfTestAsync(CancellationToken cancellationToken = default)
    {
        return SendControlCommandAsync(VoltronicProtocolConstants.SelfTestCommand, cancellationToken);
    }

    public Task<byte[]?> QueryRawAsync(string command, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                if (!IsConnected && !TryConnect())
                {
                    return null;
                }

                if (_handle == null || _handle.IsClosed)
                {
                    return null;
                }

                try
                {
                    DrainStaleBufferInternal();
                    if (!HidReportFramer.WriteCommand(_handle, command))
                    {
                        DisconnectInternal();
                        return null;
                    }

                    return ReadFramedResponse(_handle, command, cancellationToken);
                }
                catch
                {
                    DisconnectInternal();
                    return null;
                }
            }
        }, cancellationToken);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Disconnect();
    }

    private bool TryConnectViaRegistryFallback()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\HID\{HidConstants.DeviceIdSubstring}");
            if (key == null)
            {
                return false;
            }

            foreach (string subKey in key.GetSubKeyNames())
            {
                string candidatePath = $@"\\?\HID#{HidConstants.DeviceIdSubstring}#{subKey}#{HidInterfaceGuid}";
                if (TryOpenHandle(candidatePath))
                {
                    ActiveDevicePath = candidatePath;
                    DrainStaleBufferInternal();
                    return true;
                }
            }
        }
        catch
        {
            // Registry access error fallback
        }

        return false;
    }

    private bool TryOpenHandle(string path)
    {
        SafeFileHandle handle = HidOverlappedIo.OpenDeviceHandle(path);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return false;
        }

        _handle = handle;
        return true;
    }

    private void DisconnectInternal()
    {
        if (_handle != null)
        {
            if (!_handle.IsClosed && !_handle.IsInvalid)
            {
                HidOverlappedIo.CancelPendingIo(_handle);
                _handle.Dispose();
            }

            _handle = null;
        }

        ActiveDevicePath = null;
        _parser.ResetSelfTest();
    }

    private void DrainStaleBufferInternal()
    {
        if (_handle == null || _handle.IsClosed)
        {
            return;
        }

        HidOverlappedIo.DrainInputBuffer(_handle, HidConstants.DefaultDrainTimeoutMs);
    }

    private static byte[]? ReadFramedResponse(
        SafeFileHandle handle,
        string command,
        CancellationToken cancellationToken)
    {
        List<byte> response = new();
        bool requireFramingHeader = command.StartsWith(
            VoltronicProtocolConstants.QueryCommandPrefix,
            StringComparison.OrdinalIgnoreCase);

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < HidConstants.DefaultQueryTimeoutMs)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            byte[] inputReport = new byte[HidConstants.ReportLength];
            int bytesRead = HidOverlappedIo.ReadReport(handle, inputReport, HidConstants.DefaultReadTimeoutMs);
            if (bytesRead <= 1)
            {
                continue;
            }

            if (HidReportFramer.AppendReportBytes(response, inputReport.AsSpan(1, bytesRead - 1), requireFramingHeader))
            {
                return response.ToArray();
            }
        }

        return response.Count > 0 ? response.ToArray() : null;
    }

    private Task<bool> SendControlCommandAsync(string command, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                if (!IsConnected && !TryConnect())
                {
                    return false;
                }

                if (_handle == null || _handle.IsClosed)
                {
                    return false;
                }

                try
                {
                    DrainStaleBufferInternal();
                    if (!HidReportFramer.WriteCommand(_handle, command))
                    {
                        DisconnectInternal();
                        return false;
                    }

                    byte[] inputReport = new byte[HidConstants.ReportLength];
                    int bytesRead = HidOverlappedIo.ReadReport(handle: _handle, buffer: inputReport, timeoutMs: HidConstants.DefaultReadTimeoutMs);
                    if (bytesRead > 1 && inputReport[1] == VoltronicProtocolConstants.NakHeader)
                    {
                        return false;
                    }

                    return true;
                }
                catch
                {
                    DisconnectInternal();
                    return false;
                }
            }
        }, cancellationToken);
    }
}
