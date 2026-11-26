using System.Runtime.InteropServices;

namespace VoltTray.Infrastructure.Hid.Interop;

/// <summary>
/// Win32 HIDP_CAPS structure populated by HidP_GetCaps.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct HidCapabilities
{
    public ushort Usage;
    public ushort UsagePage;
    public ushort InputReportByteLength;
    public ushort OutputReportByteLength;
    public ushort FeatureReportByteLength;
    public HidCapabilitiesReservedBuffer Reserved;
    public ushort NumberLinkCollectionNodes;
    public ushort NumberInputButtonCaps;
    public ushort NumberInputValueCaps;
    public ushort NumberInputDataIndices;
    public ushort NumberOutputButtonCaps;
    public ushort NumberOutputValueCaps;
    public ushort NumberOutputDataIndices;
    public ushort NumberFeatureButtonCaps;
    public ushort NumberFeatureValueCaps;
    public ushort NumberFeatureDataIndices;
}
