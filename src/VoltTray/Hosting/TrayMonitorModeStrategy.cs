using VoltTray.Core.Ports;
using VoltTray.Infrastructure.OS;
using VoltTray.UI.Dialogs;
using VoltTray.UI.Rendering;

namespace VoltTray.Hosting;

/// <summary>
/// Executes the interactive system-tray UPS monitoring lifecycle.
/// </summary>
public sealed class TrayMonitorModeStrategy : IApplicationModeStrategy
{
    public int Execute(ApplicationOptions options)
    {
        using Mutex? mutex = SingleInstanceManager.TryAcquire();
        if (mutex == null)
        {
            return 0;
        }

        try
        {
            IUpsDevicePort upsPort = UpsDevicePortFactory.Create(options);
            var shutdownPort = new WindowsShutdownPort();
            var notificationPort = new WindowsNotificationPort();
            var trayRenderer = new TrayRenderer();
            var popoverPainter = new PopoverPainter();
            var exitDialog = new ExitDialog();

            using var lifecycle = new HostLifecycle(
                upsPort,
                shutdownPort,
                notificationPort,
                trayRenderer,
                popoverPainter,
                exitDialog);

            if (options.IsDryRun)
            {
                lifecycle.ShutdownCoordinator.DryRun = true;
            }

            lifecycle.ExitAction = () => Win32MessagePump.PostQuit(0);
            lifecycle.Start(options.PollingIntervalMs);

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
}
