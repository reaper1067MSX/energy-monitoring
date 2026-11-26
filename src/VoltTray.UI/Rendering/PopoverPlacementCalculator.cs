using VoltTray.UI.Models;
using VoltTray.UI.Tokens.Primitives;

namespace VoltTray.UI.Rendering;

/// <summary>
/// Declarative anchor-strategy geometry calculator for placing the Popover card
/// relative to any taskbar edge while clamping within the active monitor work area.
/// </summary>
internal static class PopoverPlacementCalculator
{
    private static readonly IReadOnlyDictionary<TaskbarEdge, Func<ViewRect, int, int, int, (int Left, int Top)>> EdgeAnchorStrategies =
        new Dictionary<TaskbarEdge, Func<ViewRect, int, int, int, (int Left, int Top)>>
        {
            [TaskbarEdge.Bottom] = (icon, width, height, margin) =>
                (icon.Left + (icon.Width / 2) - (width / 2), icon.Top - height - margin),
            [TaskbarEdge.Top] = (icon, width, _, margin) =>
                (icon.Left + (icon.Width / 2) - (width / 2), icon.Bottom + margin),
            [TaskbarEdge.Left] = (icon, _, height, margin) =>
                (icon.Right + margin, icon.Top + (icon.Height / 2) - (height / 2)),
            [TaskbarEdge.Right] = (icon, width, height, margin) =>
                (icon.Left - width - margin, icon.Top + (icon.Height / 2) - (height / 2))
        };

    public static ViewRect Calculate(
        ViewRect iconRect,
        TaskbarEdge edge,
        ViewRect workArea,
        int cardHeight = LayoutTokens.PopoverCardHeight)
    {
        int width = LayoutTokens.PopoverWidth;
        int margin = LayoutTokens.PopoverEdgeMarginPx;

        Func<ViewRect, int, int, int, (int Left, int Top)> strategy = EdgeAnchorStrategies.TryGetValue(edge, out var matched)
            ? matched
            : EdgeAnchorStrategies[TaskbarEdge.Bottom];

        (int rawLeft, int rawTop) = strategy(iconRect, width, cardHeight, margin);

        int minLeft = workArea.Left + margin;
        int maxLeft = Math.Max(minLeft, workArea.Right - width - margin);
        int left = Math.Clamp(rawLeft, minLeft, maxLeft);

        int minTop = workArea.Top + margin;
        int maxTop = Math.Max(minTop, workArea.Bottom - cardHeight - margin);
        int top = Math.Clamp(rawTop, minTop, maxTop);

        return new ViewRect(left, top, left + width, top + cardHeight);
    }
}
