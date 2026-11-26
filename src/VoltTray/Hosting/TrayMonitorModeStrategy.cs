using VoltTray.Core.Ports;
using VoltTray.Infrastructure.Hid;
using VoltTray.Shared;

namespace VoltTray.Hosting;

/// <summary>
/// Executes the interactive system-tray UPS monitoring lifecycle.
/// </summary>
public sealed class TrayMonitorModeStrategy : IApplicationModeStrategy
{
    private readonly TrayMonitorLaunchOptions _options;

    public TrayMonitorModeStrategy(TrayMonitorLaunchOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public int Execute()
    {
        using Mutex? mutex = SingleInstanceManager.TryAcquire();
        if (mutex == null)
        {
            return ApplicationExitCodes.Success;
        }

        try
        {
            using var bootstrapper = new TrayMonitorBootstrapper(_options, () => CreateUpsDevicePort(_options));
            HostLifecycle lifecycle = bootstrapper.Lifecycle;

            lifecycle.ExitAction = () => Win32MessagePump.PostQuit(ApplicationExitCodes.Success);
            lifecycle.Start(_options.PollingIntervalMs);

            int exitCode = Win32MessagePump.Run();
            lifecycle.Stop();
            return exitCode;
        }
        finally
        {
            try
            {
                mutex.ReleaseMutex();
            }
            catch
            {
                // Ignore abandoned or non-owned mutex state on shutdown.
            }
        }
    }

    internal static IUpsDevicePort CreateUpsDevicePort(TrayMonitorLaunchOptions options)
    {
        return options.IsSimulation
            ? new SimulatedUpsDevicePort()
            : new Win32HidUpsPort();
    }
}
