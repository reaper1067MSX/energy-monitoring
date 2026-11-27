using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace VoltTray.UI.TaskbarPill;

internal sealed class Win32FullscreenStateProvider : IFullscreenStateProvider
{
    public unsafe bool IsFullscreenActive()
    {
        try
        {
            if (PInvoke.SHQueryUserNotificationState(out QUERY_USER_NOTIFICATION_STATE state).Succeeded)
            {
                return state == QUERY_USER_NOTIFICATION_STATE.QUNS_BUSY ||
                       state == QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN ||
                       state == QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or PlatformNotSupportedException)
        {
            // Headless or unsupported OS environment fallback.
        }

        return false;
    }
}
