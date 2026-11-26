using VoltTray.Core.Diagnostics;
using VoltTray.Infrastructure.Hid;

namespace VoltTray.Infrastructure.Diagnostics;

/// <summary>
/// Port for read-only USB HID device discovery and diagnostic inquiry capture.
/// </summary>
public interface IUpsDiagnosticProbe
{
    IReadOnlyList<HidDeviceDescriptor> DiscoverDevices(bool includeAllHidDevices = false);
    UpsProbeCapture? QueryDevice(HidDeviceDescriptor device, UpsDiagnosticInquiry inquiry);
}
