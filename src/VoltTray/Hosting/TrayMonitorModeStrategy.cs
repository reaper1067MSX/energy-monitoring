using VoltTray.Core.Ports;
using VoltTray.Infrastructure.Hid;
using VoltTray.Infrastructure.OS;
using VoltTray.Shared;
using VoltTray.UI.Dialogs;
using VoltTray.UI.Rendering;

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
            using IUpsDevicePort upsPort = CreateUpsDevicePort(_options);
            var shutdownPort = new WindowsShutdownPort();
            var notificationPort = new WindowsNotificationPort();
            using var trayRenderer = new TrayRenderer();
            using var popoverPainter = new PopoverPainter();
            var exitDialog = new ExitDialog();

            using var lifecycle = new HostLifecycle(
                upsPort,
                shutdownPort,
                notificationPort,
                trayRenderer,
                popoverPainter,
                exitDialog);

            if (_options.IsDryRun)
            {
                lifecycle.ShutdownCoordinator.DryRun = true;
            }

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
