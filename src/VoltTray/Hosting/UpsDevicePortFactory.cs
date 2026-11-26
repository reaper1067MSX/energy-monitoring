using VoltTray.Core.Ports;
using VoltTray.Infrastructure.Hid;

namespace VoltTray.Hosting;

/// <summary>
/// Creates the configured <see cref="IUpsDevicePort"/> adapter for live hardware or simulation mode.
/// </summary>
public static class UpsDevicePortFactory
{
    public static IUpsDevicePort Create(ApplicationOptions options)
    {
        return options.IsSimulation
            ? new SimulatedUpsDevicePort()
            : new Win32HidUpsPort();
    }
}
