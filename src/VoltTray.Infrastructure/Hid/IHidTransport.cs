using VoltTray.Shared;

namespace VoltTray.Infrastructure.Hid;

/// <summary>
/// Opens OS-level HID connections for a discovered device path, returning rich domain errors on failure.
/// </summary>
internal interface IHidTransport
{
    Result<IHidConnection> Open(string devicePath);
}
