using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;
using VoltTray.UI.Models;
using VoltTray.UI.Tokens;

namespace VoltTray.UI.Tray;

/// <summary>
/// Resolves monitor work area and active taskbar edge using Win32 MonitorFromRect + GetMonitorInfoW.
/// </summary>
public sealed class Win32ScreenPlacementProvider : IScreenPlacementProvider
{
    public unsafe (ViewRect WorkArea, TaskbarEdge Edge) GetWorkAreaForAnchor(ViewRect anchorRect)
    {
        try
        {
            var nativeAnchor = new RECT(
                anchorRect.Left,
                anchorRect.Top,
                anchorRect.Right,
                anchorRect.Bottom);

            HMONITOR monitorHandle = PInvoke.MonitorFromRect(&nativeAnchor, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
            if (!monitorHandle.IsNull)
            {
                var monitorInfo = new MONITORINFO
                {
                    cbSize = (uint)sizeof(MONITORINFO)
                };

                if (PInvoke.GetMonitorInfo(monitorHandle, ref monitorInfo))
                {
                    var monitorBounds = new ViewRect(
                        monitorInfo.rcMonitor.left,
                        monitorInfo.rcMonitor.top,
                        monitorInfo.rcMonitor.right,
                        monitorInfo.rcMonitor.bottom);

                    var workArea = new ViewRect(
                        monitorInfo.rcWork.left,
                        monitorInfo.rcWork.top,
                        monitorInfo.rcWork.right,
                        monitorInfo.rcWork.bottom);

                    return (workArea, InferTaskbarEdge(monitorBounds, workArea));
                }
            }
        }
        catch
        {
            // Headless or non-interactive fallback handled below
        }

        int screenWidth = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSCREEN);
        int screenHeight = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYSCREEN);
        int resolvedWidth = screenWidth > 0 ? screenWidth : DesignTokens.Sizes.FallbackScreenWidthPx;
        int resolvedHeight = screenHeight > 0 ? screenHeight : DesignTokens.Sizes.FallbackScreenHeightPx;
        return (new ViewRect(0, 0, resolvedWidth, resolvedHeight), TaskbarEdge.Bottom);
    }

    public static TaskbarEdge InferTaskbarEdge(ViewRect monitorBounds, ViewRect workArea)
    {
        if (workArea.Top > monitorBounds.Top)
        {
            return TaskbarEdge.Top;
        }

        if (workArea.Left > monitorBounds.Left)
        {
            return TaskbarEdge.Left;
        }

        if (workArea.Right < monitorBounds.Right)
        {
            return TaskbarEdge.Right;
        }

        return TaskbarEdge.Bottom;
    }
}
