using System.Runtime.InteropServices;

namespace VoltTray.UI.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct NotifyIconIdentifier
{
    public uint Size;
    public IntPtr WindowHandle;
    public uint Id;
    public Guid GuidItem;
}
