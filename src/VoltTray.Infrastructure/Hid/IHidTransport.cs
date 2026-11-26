namespace VoltTray.Infrastructure.Hid;

/// <summary>
/// Opens OS-level HID connections for a discovered device path.
/// </summary>
internal interface IHidTransport
{
    IHidConnection? TryOpen(string devicePath);
}
