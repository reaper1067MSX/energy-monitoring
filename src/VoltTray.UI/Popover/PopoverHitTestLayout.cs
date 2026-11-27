using VoltTray.UI.Models;
using VoltTray.UI.Tokens.Primitives;

namespace VoltTray.UI.Popover;

public sealed class PopoverHitTestLayout
{
    public ViewRect SelfTestButton { get; }
    public ViewRect ToggleBeeperButton { get; }

    public PopoverHitTestLayout(int width, int height)
    {
        int buttonTop = height - PopoverLayoutConstants.OuterMargin - LayoutTokens.ButtonHeight;
        int buttonBottom = buttonTop + LayoutTokens.ButtonHeight;
        int contentWidth = width - (PopoverLayoutConstants.OuterMargin * 2);
        int buttonWidth = (contentWidth - PopoverLayoutConstants.ColumnGap) / 2;

        int leftButtonLeft = PopoverLayoutConstants.OuterMargin;
        int leftButtonRight = leftButtonLeft + buttonWidth;

        int rightButtonLeft = leftButtonRight + PopoverLayoutConstants.ColumnGap;
        int rightButtonRight = rightButtonLeft + buttonWidth;

        SelfTestButton = new ViewRect(leftButtonLeft, buttonTop, leftButtonRight, buttonBottom);
        ToggleBeeperButton = new ViewRect(rightButtonLeft, buttonTop, rightButtonRight, buttonBottom);
    }

    public PopoverActionId HitTest(int x, int y)
    {
        if (Contains(SelfTestButton, x, y))
        {
            return PopoverActionId.SelfTest;
        }

        if (Contains(ToggleBeeperButton, x, y))
        {
            return PopoverActionId.ToggleBeeper;
        }

        return PopoverActionId.None;
    }

    public static bool Contains(ViewRect rect, int x, int y)
    {
        return x >= rect.Left && x < rect.Right && y >= rect.Top && y < rect.Bottom;
    }
}
