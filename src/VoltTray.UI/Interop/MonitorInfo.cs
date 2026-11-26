using System.Runtime.InteropServices;

namespace VoltTray.UI.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct MonitorInfo
{
    public uint Size;
    public NativeRect MonitorBounds;
    public NativeRect WorkArea;
    public uint Flags;
}
