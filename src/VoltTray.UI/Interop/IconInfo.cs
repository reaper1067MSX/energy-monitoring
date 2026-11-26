using System.Runtime.InteropServices;

namespace VoltTray.UI.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct IconInfo
{
    public int IsIcon;
    public int HotspotX;
    public int HotspotY;
    public IntPtr MaskBitmap;
    public IntPtr ColorBitmap;
}
