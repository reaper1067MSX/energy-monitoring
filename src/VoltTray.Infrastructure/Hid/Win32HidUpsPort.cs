using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using VoltTray.Core.Ports;

namespace VoltTray.Infrastructure.Hid;

/// <summary>
/// Driverless Win32 Overlapped USB HID port for Forza FX-2200 LCD (VID_0665 PID_5161).
/// Communicates using 9-byte reports (Byte 0: 0x00 Report ID, Bytes 1..8: chunk payload).
/// Supports rapid release in under 100 ms when paused to avoid port contention.
/// </summary>
public sealed class Win32HidUpsPort : IUpsDevicePort
{
    public const string DefaultDevicePath = @"\\?\HID#VID_0665&PID_5161#7&1949aaec&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";
    private const string HidInterfaceGuid = "{4d1e55b2-f16f-11cf-88cb-001111000030}";

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
        byte[] lpBuffer,
        uint nNumberOfBytesToWrite,
        out uint lpNumberOfBytesWritten,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(
        SafeFileHandle hFile,
        byte[] lpBuffer,
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
    private SafeFileHandle? _handle;
    private bool _isDisposed;

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

            string targetPath = customPath ?? DefaultDevicePath;
            if (TryOpenHandle(targetPath))
            {
                ActiveDevicePath = targetPath;
                DrainStaleBufferInternal();
                return true;
            }

            // Fallback: Registry enumeration
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\HID\VID_0665&PID_5161");
                if (key != null)
                {
                    foreach (string subKey in key.GetSubKeyNames())
                    {
                        string candidatePath = $@"\\?\HID#VID_0665&PID_5161#{subKey}#{HidInterfaceGuid}";
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
    }

    private void DrainStaleBufferInternal()
    {
        if (_handle == null || _handle.IsClosed) return;
        byte[] drainBuf = new byte[9];
        while (ReadOverlapped(_handle, drainBuf, 25) > 0) { }
    }

    public Task<string?> QueryAsync(string command, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                if (!IsConnected && !TryConnect())
                    return null;

                if (_handle == null || _handle.IsClosed) return null;

                try
                {
                    DrainStaleBufferInternal();

                    byte[] cmdBytes = Encoding.ASCII.GetBytes(command);
                    int chunkLen = 8;
                    for (int i = 0; i < cmdBytes.Length; i += chunkLen)
                    {
                        byte[] report = new byte[9];
                        report[0] = 0x00; // Report ID
                        int count = Math.Min(chunkLen, cmdBytes.Length - i);
                        Array.Copy(cmdBytes, i, report, 1, count);
                        if (!WriteOverlapped(_handle, report, 1000))
                        {
                            DisconnectInternal();
                            return null;
                        }
                    }

                    List<byte> response = new();
                    bool expectHeader = command.StartsWith("Q", StringComparison.OrdinalIgnoreCase);
                    var sw = Stopwatch.StartNew();
                    while (sw.ElapsedMilliseconds < 1500)
                    {
                        if (cancellationToken.IsCancellationRequested) return null;

                        byte[] inReport = new byte[9];
                        int read = ReadOverlapped(_handle, inReport, 200);
                        if (read > 0)
                        {
                            for (int b = 1; b < read; b++)
                            {
                                byte val = inReport[b];
                                if (val == 0) continue;

                                if (response.Count == 0 && expectHeader && val != 0x23 && val != 0x28)
                                {
                                    // Discard stale unconsumed bytes from a previous interrupted query
                                    continue;
                                }

                                response.Add(val);
                                if (val == 0x0D) goto Done; // CR delimiter
                            }
                        }
                        else if (response.Count > 0)
                        {
                            break;
                        }
                    }

                Done:
                    return response.Count > 0 ? Encoding.Latin1.GetString(response.ToArray()) : null;
                }
                catch
                {
                    DisconnectInternal();
                    return null;
                }
            }
        }, cancellationToken);
    }

    public async Task<bool> SendCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        var response = await QueryAsync(command, cancellationToken);
        return response != null;
    }

    private static int ReadOverlapped(SafeFileHandle handle, byte[] buffer, int timeoutMs)
    {
        IntPtr hEvent = CreateEvent(IntPtr.Zero, true, false, null);
        OVERLAPPED ov = new() { hEvent = hEvent };
        IntPtr pOv = Marshal.AllocHGlobal(Marshal.SizeOf(ov));
        Marshal.StructureToPtr(ov, pOv, false);

        try
        {
            bool ok = ReadFile(handle, buffer, (uint)buffer.Length, out uint read, pOv);
            if (!ok && Marshal.GetLastWin32Error() == 997) // ERROR_IO_PENDING
            {
                uint wait = WaitForSingleObject(hEvent, (uint)timeoutMs);
                if (wait == 0) // WAIT_OBJECT_0
                {
                    GetOverlappedResult(handle, pOv, out read, false);
                    return (int)read;
                }
                else
                {
                    CancelIoEx(handle, pOv);
                    return 0;
                }
            }
            return (int)read;
        }
        finally
        {
            CloseHandle(hEvent);
            Marshal.FreeHGlobal(pOv);
        }
    }

    private static bool WriteOverlapped(SafeFileHandle handle, byte[] buffer, int timeoutMs)
    {
        IntPtr hEvent = CreateEvent(IntPtr.Zero, true, false, null);
        OVERLAPPED ov = new() { hEvent = hEvent };
        IntPtr pOv = Marshal.AllocHGlobal(Marshal.SizeOf(ov));
        Marshal.StructureToPtr(ov, pOv, false);

        try
        {
            bool ok = WriteFile(handle, buffer, (uint)buffer.Length, out uint written, pOv);
            if (!ok && Marshal.GetLastWin32Error() == 997) // ERROR_IO_PENDING
            {
                uint wait = WaitForSingleObject(hEvent, (uint)timeoutMs);
                if (wait == 0) // WAIT_OBJECT_0
                {
                    GetOverlappedResult(handle, pOv, out written, false);
                    return written == buffer.Length;
                }
                else
                {
                    CancelIoEx(handle, pOv);
                    return false;
                }
            }
            return ok;
        }
        finally
        {
            CloseHandle(hEvent);
            Marshal.FreeHGlobal(pOv);
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Disconnect();
    }
}
