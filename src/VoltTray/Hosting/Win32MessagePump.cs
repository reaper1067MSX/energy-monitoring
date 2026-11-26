using VoltTray.Hosting.Interop;

namespace VoltTray.Hosting;

/// <summary>
/// Runs the Win32 GUI message loop for the tray host process.
/// </summary>
internal static class Win32MessagePump
{
    public static int Run()
    {
        User32MessageNativeMethods.NativeMessage message = default;
        int status;

        while ((status = User32MessageNativeMethods.GetMessage(out message, IntPtr.Zero, 0, 0)) != 0)
        {
            if (status == User32MessageNativeMethods.GetMessageErrorResult)
            {
                break;
            }

            User32MessageNativeMethods.TranslateMessage(ref message);
            User32MessageNativeMethods.DispatchMessage(ref message);
        }

        return (int)message.WParam;
    }

    public static void PostQuit(int exitCode = 0)
    {
        User32MessageNativeMethods.PostQuitMessage(exitCode);
    }
}
