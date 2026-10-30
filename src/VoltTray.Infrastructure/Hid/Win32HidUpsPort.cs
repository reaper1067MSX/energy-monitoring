using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
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

    public async Task<string?> QueryAsync(string command, CancellationToken cancellationToken = default)
    {
        if (!IsConnected && !TryConnect())
            return null;

        return await Task.Run(() =>
        {
            lock (_lock)
            {
                if (_handle == null || _handle.IsClosed) return null;

                try
                {
                    using var stream = new FileStream(_handle, FileAccess.ReadWrite, 9, isAsync: false);

                    // Send command in 8-byte chunks (Byte 0 = 0x00 Report ID)
                    byte[] cmdBytes = Encoding.ASCII.GetBytes(command);
                    int offset = 0;
                    while (offset < cmdBytes.Length)
                    {
                        byte[] report = new byte[9];
                        report[0] = 0x00;
                        int chunk = Math.Min(8, cmdBytes.Length - offset);
                        Array.Copy(cmdBytes, offset, report, 1, chunk);
                        stream.Write(report, 0, 9);
                        offset += chunk;
                    }

                    // Read response until \r (0x0D)
                    var sb = new StringBuilder();
                    byte[] inReport = new byte[9];
                    DateTime deadline = DateTime.UtcNow.AddMilliseconds(2000);

                    while (DateTime.UtcNow < deadline)
                    {
                        if (cancellationToken.IsCancellationRequested) return null;

                        int read = stream.Read(inReport, 0, 9);
                        if (read <= 0) break;

                        for (int i = 1; i < read; i++)
                        {
                            byte b = inReport[i];
                            if (b == 0x00) continue;
                            sb.Append((char)b);
                            if (b == 0x0D) return sb.ToString();
                        }
                    }

                    return sb.Length > 0 ? sb.ToString() : null;
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

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Disconnect();
    }
}
