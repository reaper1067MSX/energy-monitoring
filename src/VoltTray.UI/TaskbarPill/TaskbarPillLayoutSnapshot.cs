using VoltTray.UI.Interop;
using VoltTray.UI.Models;

namespace VoltTray.UI.TaskbarPill;

public sealed record TaskbarPillLayoutSnapshot(
    ViewRect TaskbarRect,
    ViewRect? TrayNotifyRect,
    ViewRect? StartButtonRect,
    ViewRect? WidgetRect,
    ViewRect? AppListRect,
    int PillWidth = Win32UiConstants.PillDefaultWidthPx,
    int PillHeight = Win32UiConstants.PillDefaultHeightPx,
    int RightMarginFallbackPx = Win32UiConstants.PillDefaultRightMarginFallbackPx);
