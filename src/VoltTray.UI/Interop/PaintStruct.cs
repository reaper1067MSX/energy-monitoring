using System.Runtime.InteropServices;

namespace VoltTray.UI.Interop;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PaintStruct
{
    public IntPtr Hdc;
    public int Erase;
    public NativeRect PaintRect;
    public int Restore;
    public int IncUpdate;
    public fixed byte Reserved[32];
}
