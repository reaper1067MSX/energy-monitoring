using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
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

    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_OVERLAPPED = 0x40000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct OVERLAPPED
    {
        public IntPtr Internal;
        public IntPtr InternalHigh;
        public uint Offset;
        public uint OffsetHigh;
        public IntPtr hEvent;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(
        SafeFileHandle hFile,
        IntPtr lpBuffer,
        uint nNumberOfBytesToWrite,
        out uint lpNumberOfBytesWritten,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(
        SafeFileHandle hFile,
        IntPtr lpBuffer,
        uint nNumberOfBytesToRead,
        out uint lpNumberOfBytesRead,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetOverlappedResult(
        SafeFileHandle hFile,
        IntPtr lpOverlapped,
        out uint lpNumberOfBytesTransferred,
        bool bWait);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CancelIoEx(SafeFileHandle hFile, IntPtr lpOverlapped);

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
            if (IsConnected) return true;

            DisconnectInternal();

            string targetPath = customPath ?? _defaultDevicePath;
            if (TryOpenHandle(targetPath))
            {
                ActiveDevicePath = targetPath;
                DrainStaleBufferInternal();
                return true;
            }

            if (customPath != null || !_allowRegistryFallback) return false;

            // Fallback: Registry enumeration
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\HID\{HidConstants.DeviceIdSubstring}");
                if (key != null)
                {
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
            }
            catch
            {
                // Registry access error fallback
            }

            return false;
        }
    }

    private bool TryOpenHandle(string path)
    {
        var handle = CreateFile(
            path,
            GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_OVERLAPPED,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            handle.Dispose();
            return false;
        }

        _handle = handle;
        return true;
    }

    public void Disconnect()
    {
        lock (_lock)
        {
            DisconnectInternal();
        }
    }

    private void DisconnectInternal()
    {
        if (_handle != null)
        {
            if (!_handle.IsClosed && !_handle.IsInvalid)
            {
                CancelIoEx(_handle, IntPtr.Zero);
                _handle.Dispose();
            }
            _handle = null;
        }
        ActiveDevicePath = null;
        _parser.ResetSelfTest();
    }

    private void DrainStaleBufferInternal()
    {
        if (_handle == null || _handle.IsClosed) return;
        byte[] drainBuf = new byte[HidConstants.ReportLength];
        while (ReadOverlapped(_handle, drainBuf, HidConstants.DefaultDrainTimeoutMs) > 0) { }
    }

    public async Task<UpsTelemetry?> ReadTelemetryAsync(DateTimeOffset? now = null, CancellationToken cancellationToken = default)
    {
        byte[]? raw = await QueryRawAsync(VoltronicProtocolConstants.QueryStatusCommand, cancellationToken);
        if (raw is null or { Length: 0 }) return null;

        var parsed = _parser.Parse(raw, _lastTelemetry, now);
        if (parsed != null) _lastTelemetry = parsed;
        return parsed;
    }

    public Task<bool> ToggleBeeperAsync(CancellationToken cancellationToken = default) =>
        SendControlCommandAsync(VoltronicProtocolConstants.BeeperToggleCommand, cancellationToken);

    public Task<bool> TriggerSelfTestAsync(CancellationToken cancellationToken = default) =>
        SendControlCommandAsync(VoltronicProtocolConstants.SelfTestCommand, cancellationToken);

    public Task<byte[]?> QueryRawAsync(string command, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                if (!IsConnected && !TryConnect()) return null;
                if (_handle == null || _handle.IsClosed) return null;

                try
                {
                    DrainStaleBufferInternal();
                    if (!WriteCommandChunksInternal(_handle, command))
                    {
                        DisconnectInternal();
                        return null;
                    }

                    List<byte> response = new();
                    bool expectHeader = command.StartsWith(VoltronicProtocolConstants.QueryCommandPrefix, StringComparison.OrdinalIgnoreCase);
                    var sw = Stopwatch.StartNew();
                    while (sw.ElapsedMilliseconds < HidConstants.DefaultQueryTimeoutMs)
                    {
                        if (cancellationToken.IsCancellationRequested) return null;

                        byte[] inReport = new byte[HidConstants.ReportLength];
                        int read = ReadOverlapped(_handle, inReport, HidConstants.DefaultReadTimeoutMs);
                        if (read > 0)
                        {
                            for (int b = 1; b < read; b++)
                            {
                                byte val = inReport[b];
                                if (response.Count == 0)
                                {
                                    if (expectHeader && val != VoltronicProtocolConstants.FrameHeaderHash && val != VoltronicProtocolConstants.FrameHeaderParen)
                                        continue;
                                    if (val == VoltronicProtocolConstants.NullByte)
                                        continue;
                                }

                                response.Add(val);
                                if (val == VoltronicProtocolConstants.CarriageReturn) return response.ToArray();
                            }
                        }
                    }

                    return response.Count > 0 ? response.ToArray() : null;
                }
                catch
                {
                    DisconnectInternal();
                    return null;
                }
            }
        }, cancellationToken);
    }

    private Task<bool> SendControlCommandAsync(string command, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                if (cancellationToken.IsCancellationRequested) return false;
                if (!IsConnected && !TryConnect()) return false;
                if (_handle == null || _handle.IsClosed) return false;

                try
                {
                    DrainStaleBufferInternal();
                    if (!WriteCommandChunksInternal(_handle, command))
                    {
                        DisconnectInternal();
                        return false;
                    }

                    byte[] inReport = new byte[HidConstants.ReportLength];
                    int read = ReadOverlapped(_handle, inReport, HidConstants.DefaultReadTimeoutMs);
                    if (read > 1 && inReport[1] == VoltronicProtocolConstants.NakHeader)
                        return false;

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

    private static bool WriteCommandChunksInternal(SafeFileHandle handle, string command)
    {
        byte[] cmdBytes = Encoding.ASCII.GetBytes(command);
        int chunkLen = HidConstants.PayloadChunkLength;
        for (int i = 0; i < cmdBytes.Length; i += chunkLen)
        {
            byte[] report = new byte[HidConstants.ReportLength];
            report[0] = HidConstants.DefaultReportId;
            int count = Math.Min(chunkLen, cmdBytes.Length - i);
            Array.Copy(cmdBytes, i, report, 1, count);
            if (!WriteOverlapped(handle, report, HidConstants.DefaultWriteTimeoutMs))
                return false;
        }
        return true;
    }

    private static int ReadOverlapped(SafeFileHandle handle, byte[] buffer, int timeoutMs)
    {
        IntPtr hEvent = CreateEvent(IntPtr.Zero, true, false, null);
        IntPtr pOv = Marshal.AllocHGlobal(Marshal.SizeOf<OVERLAPPED>());
        IntPtr pBuf = Marshal.AllocHGlobal(buffer.Length);
        Marshal.StructureToPtr(new OVERLAPPED { hEvent = hEvent }, pOv, false);

        try
        {
            if (!ReadFile(handle, pBuf, (uint)buffer.Length, out uint read, pOv) &&
                !WaitOverlappedCompletion(handle, hEvent, pOv, timeoutMs, out read))
                return 0;

            int count = (int)Math.Min(read, (uint)buffer.Length);
            if (count > 0) Marshal.Copy(pBuf, buffer, 0, count);
            return count;
        }
        finally
        {
            CloseHandle(hEvent);
            Marshal.FreeHGlobal(pOv);
            Marshal.FreeHGlobal(pBuf);
        }
    }

    private static bool WriteOverlapped(SafeFileHandle handle, byte[] buffer, int timeoutMs)
    {
        IntPtr hEvent = CreateEvent(IntPtr.Zero, true, false, null);
        IntPtr pOv = Marshal.AllocHGlobal(Marshal.SizeOf<OVERLAPPED>());
        IntPtr pBuf = Marshal.AllocHGlobal(buffer.Length);
        Marshal.StructureToPtr(new OVERLAPPED { hEvent = hEvent }, pOv, false);
        Marshal.Copy(buffer, 0, pBuf, buffer.Length);

        try
        {
            if (!WriteFile(handle, pBuf, (uint)buffer.Length, out uint written, pOv) &&
                !WaitOverlappedCompletion(handle, hEvent, pOv, timeoutMs, out written))
                return false;

            return written == buffer.Length;
        }
        finally
        {
            CloseHandle(hEvent);
            Marshal.FreeHGlobal(pOv);
            Marshal.FreeHGlobal(pBuf);
        }
    }

    private static bool WaitOverlappedCompletion(SafeFileHandle handle, IntPtr hEvent, IntPtr pOv, int timeoutMs, out uint transferred)
    {
        transferred = 0;
        if (Marshal.GetLastWin32Error() != 997) return false; // ERROR_IO_PENDING

        if (WaitForSingleObject(hEvent, (uint)timeoutMs) == 0) // WAIT_OBJECT_0
            return GetOverlappedResult(handle, pOv, out transferred, false);

        CancelIoEx(handle, pOv);
        GetOverlappedResult(handle, pOv, out _, true);
        return false;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Disconnect();
    }
}
