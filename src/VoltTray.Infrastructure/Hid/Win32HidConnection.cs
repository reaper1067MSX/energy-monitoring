using Microsoft.Win32.SafeHandles;

namespace VoltTray.Infrastructure.Hid;

/// <summary>
/// Win32 Overlapped <see cref="IHidConnection"/> backed by a <see cref="SafeFileHandle"/>.
/// </summary>
internal sealed class Win32HidConnection : IHidConnection
{
    private readonly SafeFileHandle _handle;

    public Win32HidConnection(SafeFileHandle handle)
    {
        _handle = handle;
    }

    public bool IsOpen => !_handle.IsInvalid && !_handle.IsClosed;

    public void CancelPendingIo()
    {
        HidOverlappedIo.CancelPendingIo(_handle);
    }

    public void DrainInputBuffer(int drainTimeoutMs)
    {
        HidOverlappedIo.DrainInputBuffer(_handle, drainTimeoutMs);
    }

    public bool WriteCommand(string command)
    {
        return HidReportFramer.WriteCommand(_handle, command);
    }

    public int ReadReport(byte[] buffer, int timeoutMs)
    {
        return HidOverlappedIo.ReadReport(_handle, buffer, timeoutMs);
    }

    public void Dispose()
    {
        _handle.Dispose();
    }
}
