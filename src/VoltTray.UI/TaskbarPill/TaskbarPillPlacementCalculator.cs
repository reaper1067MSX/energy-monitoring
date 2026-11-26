using VoltTray.UI.Interop;
using VoltTray.UI.Models;

namespace VoltTray.UI.TaskbarPill;

/// <summary>
/// Computes non-overlapping taskbar pill bounds anchored left of TrayNotifyWnd,
/// with Win11 24H2 cold-boot fallback and Start-button vertical centering.
/// </summary>
public sealed class TaskbarPillPlacementCalculator : ITaskbarPillPlacementCalculator
{
    public ViewRect Calculate(TaskbarPillLayoutSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        int gap = Win32UiConstants.PillEdgeGapPx;
        int rawLeft = snapshot.TrayNotifyRect is ViewRect notify && notify.Width > 0
            ? notify.Left - snapshot.PillWidth - gap
            : snapshot.TaskbarRect.Right - snapshot.RightMarginFallbackPx - snapshot.PillWidth;

        int minLeft = snapshot.TaskbarRect.Left + gap;
        if (snapshot.WidgetRect is ViewRect widget && widget.Width > 0)
        {
            minLeft = Math.Max(minLeft, widget.Right + gap);
        }

        if (snapshot.AppListRect is ViewRect apps && apps.Width > 0)
        {
            minLeft = Math.Max(minLeft, apps.Right + gap);
        }

        int maxLeft = Math.Max(minLeft, snapshot.TaskbarRect.Right - snapshot.PillWidth - gap);
        int left = Math.Clamp(rawLeft, minLeft, maxLeft);

        int rawTop = snapshot.StartButtonRect is ViewRect start && start.Height > 0
            ? start.Top + ((start.Height - snapshot.PillHeight) / 2)
            : snapshot.TaskbarRect.Top + ((snapshot.TaskbarRect.Height - snapshot.PillHeight) / 2);

        int minTop = snapshot.TaskbarRect.Top;
        int maxTop = Math.Max(minTop, snapshot.TaskbarRect.Bottom - snapshot.PillHeight);
        int top = Math.Clamp(rawTop, minTop, maxTop);

        return new ViewRect(left, top, left + snapshot.PillWidth, top + snapshot.PillHeight);
    }
}
