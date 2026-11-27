using VoltTray.UI.Interop;
using VoltTray.UI.Models;

namespace VoltTray.UI.TaskbarPill;

/// <summary>
/// Computes non-overlapping taskbar pill bounds anchored left of TrayNotifyWnd,
/// with Win11 24H2 cold-boot fallback, width clamping, and Start-button vertical centering.
/// </summary>
internal sealed class TaskbarPillPlacementCalculator : ITaskbarPillPlacementCalculator
{
    public ViewRect Calculate(TaskbarPillLayoutSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        int gap = Win32UiConstants.PillEdgeGapPx;
        int minLeft = snapshot.TaskbarRect.Left + gap;
        if (snapshot.WidgetRect is ViewRect widget && widget.Width > 0)
        {
            minLeft = Math.Max(minLeft, widget.Right + gap);
        }

        if (snapshot.AppListRect is ViewRect apps && apps.Width > 0)
        {
            minLeft = Math.Max(minLeft, apps.Right + gap);
        }

        int rightLimit = snapshot.TrayNotifyRect is ViewRect notify && notify.Width > 0
            ? notify.Left - gap
            : snapshot.TaskbarRect.Right - gap;
        rightLimit = Math.Max(minLeft + 1, rightLimit);

        int availableWidth = Math.Max(1, rightLimit - minLeft);
        int effectiveWidth = Math.Min(snapshot.PillWidth, availableWidth);

        int rawLeft = snapshot.TrayNotifyRect is ViewRect validNotify && validNotify.Width > 0
            ? validNotify.Left - effectiveWidth - gap
            : snapshot.TaskbarRect.Right - snapshot.RightMarginFallbackPx - effectiveWidth;

        int maxLeft = Math.Max(minLeft, rightLimit - effectiveWidth);
        int left = Math.Clamp(rawLeft, minLeft, maxLeft);

        int rawTop = snapshot.StartButtonRect is ViewRect start && start.Height > 0
            ? start.Top + ((start.Height - snapshot.PillHeight) / 2)
            : snapshot.TaskbarRect.Top + ((snapshot.TaskbarRect.Height - snapshot.PillHeight) / 2);

        int minTop = snapshot.TaskbarRect.Top;
        int maxTop = Math.Max(minTop, snapshot.TaskbarRect.Bottom - snapshot.PillHeight);
        int top = Math.Clamp(rawTop, minTop, maxTop);

        return new ViewRect(left, top, left + effectiveWidth, top + snapshot.PillHeight);
    }
}
