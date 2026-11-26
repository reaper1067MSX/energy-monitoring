using System.Runtime.InteropServices;

namespace VoltTray.Infrastructure.Hid.Interop;

/// <summary>
/// Win32 OVERLAPPED structure for asynchronous HID I/O operations.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeOverlappedIo
{
    public IntPtr Internal;
    public IntPtr InternalHigh;
    public uint Offset;
    public uint OffsetHigh;
    public IntPtr EventHandle;
}
