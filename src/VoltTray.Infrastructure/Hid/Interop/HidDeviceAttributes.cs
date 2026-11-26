using System.Runtime.InteropServices;

namespace VoltTray.Infrastructure.Hid.Interop;

/// <summary>
/// Win32 HIDD_ATTRIBUTES structure populated by HidD_GetAttributes.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct HidDeviceAttributes
{
    public int Size;
    public ushort VendorId;
    public ushort ProductId;
    public ushort VersionNumber;
}
