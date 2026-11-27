using VoltTray.UI.Models;

namespace VoltTray.UI.Popover;

public sealed record PopoverRenderState(
    PopoverViewModel ViewModel,
    UITheme Theme,
    int Width,
    int Height,
    PopoverActionId HoveredAction = PopoverActionId.None,
    PopoverActionId PressedAction = PopoverActionId.None,
    int FocusedActionIndex = 0);
