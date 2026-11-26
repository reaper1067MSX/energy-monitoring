using VoltTray.UI.Interop;
using VoltTray.UI.Models;
using VoltTray.UI.Tokens;
using VoltTray.UI.Views;

namespace VoltTray.UI.Rendering;

/// <summary>
/// Resolves monitor work area and active taskbar edge using Win32 MonitorFromRect + GetMonitorInfoW.
/// </summary>
public sealed class Win32ScreenPlacementProvider : IScreenPlacementProvider
{
    public unsafe (ViewRect WorkArea, TaskbarEdge Edge) GetWorkAreaForAnchor(ViewRect anchorRect)
    {
        try
        {
            var nativeAnchor = new GdiNative.NativeRect(
                anchorRect.Left,
                anchorRect.Top,
                anchorRect.Right,
                anchorRect.Bottom);

            IntPtr monitorHandle = GdiNative.MonitorFromRect(ref nativeAnchor, GdiNative.MONITOR_DEFAULTTONEAREST);
            if (monitorHandle != IntPtr.Zero)
            {
                var monitorInfo = new GdiNative.MonitorInfo
                {
                    Size = (uint)sizeof(GdiNative.MonitorInfo)
                };

                if (GdiNative.GetMonitorInfo(monitorHandle, ref monitorInfo))
                {
                    var monitorBounds = new ViewRect(
                        monitorInfo.MonitorBounds.Left,
                        monitorInfo.MonitorBounds.Top,
                        monitorInfo.MonitorBounds.Right,
                        monitorInfo.MonitorBounds.Bottom);

                    var workArea = new ViewRect(
                        monitorInfo.WorkArea.Left,
                        monitorInfo.WorkArea.Top,
                        monitorInfo.WorkArea.Right,
                        monitorInfo.WorkArea.Bottom);

                    return (workArea, InferTaskbarEdge(monitorBounds, workArea));
                }
            }
        }
        catch
        {
            // Headless or non-interactive fallback handled below
        }

        int screenWidth = GdiNative.GetSystemMetrics(GdiNative.SM_CXSCREEN);
        int screenHeight = GdiNative.GetSystemMetrics(GdiNative.SM_CYSCREEN);
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
