using System.Runtime.InteropServices;

namespace VoltTray.Hosting.Interop;

/// <summary>
/// Win32 MSG structure processed by the tray GUI message pump.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeMessage
{
    public IntPtr Hwnd;
    public uint Message;
    public UIntPtr WParam;
    public IntPtr LParam;
    public uint Time;
    public int PointX;
    public int PointY;
    public uint Private;
}
