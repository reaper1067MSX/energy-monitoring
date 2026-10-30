using System.Runtime.InteropServices;
using VoltTray.Infrastructure.Hid;
using VoltTray.Infrastructure.OS;
using VoltTray.UI.Dialogs;
using VoltTray.UI.Rendering;

namespace VoltTray;

public static class SingleInstanceManager
{
    public const string DefaultMutexName = @"Global\VoltTray_SingleInstance_Mutex";
    public static Mutex? TryAcquire(string name = DefaultMutexName)
    {
        try { var m = new Mutex(true, name, out bool c); if (!c) { m.Dispose(); return null; } return m; }
        catch (UnauthorizedAccessException) {
            try { string l = name.StartsWith(@"Global\", StringComparison.OrdinalIgnoreCase) ? name[7..] : name; var m = new Mutex(true, l, out bool c); if (!c) { m.Dispose(); return null; } return m; } catch { return null; }
        }
        catch { return null; }
    }
}

public static class Program
{
    [StructLayout(LayoutKind.Sequential)]
    public struct MSG { public IntPtr hwnd; public uint message; public UIntPtr wParam; public IntPtr lParam; public uint time; public int pt_x, pt_y; public uint lPrivate; }
    [DllImport("user32.dll", EntryPoint = "GetMessageW")] public static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll", EntryPoint = "TranslateMessage")] public static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")] public static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll", EntryPoint = "PostQuitMessage")] public static extern void PostQuitMessage(int exitCode);

    [STAThread]
    public static int Main(string[] args)
    {
        using var mutex = SingleInstanceManager.TryAcquire();
        if (mutex == null) return 0;
        try
        {
            using var lifecycle = new HostLifecycle(new Win32HidUpsPort(), new WindowsShutdownPort(), new WindowsNotificationPort(), new TrayRenderer(), new PopoverPainter(), new ExitDialog());
            lifecycle.ExitAction = () => PostQuitMessage(0);
            lifecycle.Start();
            int exitCode = RunMessageLoop();
            lifecycle.Stop();
            return exitCode;
        }
        finally { try { mutex.ReleaseMutex(); } catch { } }
    }

    public static int RunMessageLoop()
    {
        MSG msg = default; int status;
        while ((status = GetMessage(out msg, IntPtr.Zero, 0, 0)) != 0)
        {
            if (status == -1) break;
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        return (int)msg.wParam;
    }
}
