using VoltTray.UI.Interop;

namespace VoltTray.UI.TaskbarPill;

public sealed class Win32FullscreenStateProvider : IFullscreenStateProvider
{
    public bool IsFullscreenActive()
    {
        try
        {
            if (GdiNative.SHQueryUserNotificationState(out int state) == 0)
            {
                return state == Win32UiConstants.QunsBusy ||
                       state == Win32UiConstants.QunsRunningD3dFullScreen ||
                       state == Win32UiConstants.QunsPresentationMode;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or PlatformNotSupportedException)
        {
            // Headless or unsupported OS environment fallback.
        }

        return false;
    }
}
