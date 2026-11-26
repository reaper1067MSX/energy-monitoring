using System.Runtime.InteropServices;

namespace VoltTray.UI.Interop;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WindowClassExW
{
    public uint Size;
    public uint Style;
    public IntPtr WindowProc;
    public int ClassExtra;
    public int WindowExtra;
    public IntPtr Instance;
    public IntPtr Icon;
    public IntPtr Cursor;
    public IntPtr BackgroundBrush;
    public char* MenuName;
    public char* ClassName;
    public IntPtr SmallIcon;
}
