using System.Runtime.InteropServices;

namespace VoltTray.Infrastructure.Hid.Interop;

/// <summary>
/// Win32 SP_DEVICE_INTERFACE_DATA structure returned by SetupDiEnumDeviceInterfaces.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DeviceInterfaceData
{
    public int Size;
    public Guid InterfaceClassGuid;
    public int Flags;
    public IntPtr Reserved;
}
