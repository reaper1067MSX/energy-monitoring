namespace VoltTray.Infrastructure.Hid;

/// <summary>
/// Discovers attached USB HID device interfaces dynamically without hardcoded port paths.
/// </summary>
public interface IHidDeviceScanner
{
    IReadOnlyList<HidDeviceDescriptor> DiscoverDevices(bool includeAllHidDevices = false);
}
