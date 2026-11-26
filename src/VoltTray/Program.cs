using System.Runtime.InteropServices;
using VoltTray.Cli;
using VoltTray.Core.Ports;
using VoltTray.Infrastructure.Hid;
using VoltTray.Infrastructure.OS;
using VoltTray.UI.Dialogs;
using VoltTray.UI.Rendering;

namespace VoltTray;

public static class Program
{
    private const int DefaultPollingIntervalMs = 1500;
    private const int MinimumPollingIntervalMs = 250;

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeMessage
    {
        public IntPtr Hwnd;
        public uint Message;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PointX;
        public int PointY;
        public uint Private;
    }

    [DllImport("user32.dll", EntryPoint = "GetMessageW")]
    public static extern int GetMessage(out NativeMessage message, IntPtr windowHandle, uint filterMin, uint filterMax);

    [DllImport("user32.dll", EntryPoint = "TranslateMessage")]
    public static extern bool TranslateMessage(ref NativeMessage message);

    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
    public static extern IntPtr DispatchMessage(ref NativeMessage message);

    [DllImport("user32.dll", EntryPoint = "PostQuitMessage")]
    public static extern void PostQuitMessage(int exitCode);

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Any(arg => arg.Equals("--inspect", StringComparison.OrdinalIgnoreCase)))
        {
            return InspectCommandRunner.Run(args, Console.Out);
        }

        bool isSimulation = args.Any(arg =>
            arg.Equals("--simulate", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("--sim", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("--mock", StringComparison.OrdinalIgnoreCase));

        bool isDryRun = args.Any(arg => arg.Equals("--dry-run", StringComparison.OrdinalIgnoreCase));
        int intervalMs = ParsePollingIntervalMs(args);

        using Mutex? mutex = SingleInstanceManager.TryAcquire();
        if (mutex == null)
        {
            return 0;
        }

        try
        {
            IUpsDevicePort upsPort = isSimulation ? new SimulatedUpsDevicePort() : new Win32HidUpsPort();
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

            if (isDryRun)
            {
                lifecycle.ShutdownCoordinator.DryRun = true;
            }

            lifecycle.ExitAction = () => PostQuitMessage(0);
            lifecycle.Start(intervalMs);

            int exitCode = RunMessageLoop();
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

    public static int RunMessageLoop()
    {
        NativeMessage message = default;
        int status;

        while ((status = GetMessage(out message, IntPtr.Zero, 0, 0)) != 0)
        {
            if (status == -1)
            {
                break;
            }

            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }

        return (int)message.WParam;
    }

    private static int ParsePollingIntervalMs(string[] args)
    {
        int intervalMs = DefaultPollingIntervalMs;

        for (int index = 0; index < args.Length - 1; index++)
        {
            if (args[index].Equals("--interval", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[index + 1], out int parsedInterval))
            {
                intervalMs = Math.Max(MinimumPollingIntervalMs, parsedInterval);
            }
        }

        return intervalMs;
    }
}
