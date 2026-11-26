using Microsoft.Win32.SafeHandles;

namespace VoltTray.Infrastructure.Hid;

/// <summary>
/// Opens Win32 Overlapped HID connections via <see cref="HidOverlappedIo"/>.
/// </summary>
internal sealed class Win32HidTransport : IHidTransport
{
    public IHidConnection? TryOpen(string devicePath)
    {
        SafeFileHandle handle = HidOverlappedIo.OpenDeviceHandle(devicePath);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }

        return new Win32HidConnection(handle);
    }
}
