using VoltTray.UI.Models;
using VoltTray.UI.Tokens.Primitives;

namespace VoltTray.UI.Popover;

public sealed class PopoverHitTestLayout
{
    public ViewRect SelfTestButton { get; }
    public ViewRect ToggleBeeperButton { get; }
    public ViewRect HistoryTab5Days { get; }
    public ViewRect HistoryTabMonth { get; }
    public ViewRect HistoryTabOutages { get; }

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

        int historyTop = PopoverLayoutConstants.OuterMargin +
                         PopoverLayoutConstants.HeaderHeight +
                         PopoverLayoutConstants.SectionGap +
                         PopoverLayoutConstants.SubCardHeight +
                         PopoverLayoutConstants.SectionGap +
                         PopoverLayoutConstants.LoadBandHeight +
                         PopoverLayoutConstants.SectionGap;
        int tabTop = historyTop + PopoverLayoutConstants.HistoryCardPadding - 2;
        int tabBottom = tabTop + PopoverLayoutConstants.HistoryTabHeight;
        int tabRightEdge = PopoverLayoutConstants.OuterMargin + contentWidth - PopoverLayoutConstants.HistoryCardPadding;

        int outagesLeft = tabRightEdge - PopoverLayoutConstants.HistoryTabOutagesWidth;
        HistoryTabOutages = new ViewRect(outagesLeft, tabTop, tabRightEdge, tabBottom);

        int monthRight = outagesLeft - PopoverLayoutConstants.HistoryTabGap;
        int monthLeft = monthRight - PopoverLayoutConstants.HistoryTabMonthWidth;
        HistoryTabMonth = new ViewRect(monthLeft, tabTop, monthRight, tabBottom);

        int fiveDaysRight = monthLeft - PopoverLayoutConstants.HistoryTabGap;
        int fiveDaysLeft = fiveDaysRight - PopoverLayoutConstants.HistoryTab5DaysWidth;
        HistoryTab5Days = new ViewRect(fiveDaysLeft, tabTop, fiveDaysRight, tabBottom);
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

        if (Contains(HistoryTab5Days, x, y))
        {
            return PopoverActionId.HistoryTab5Days;
        }

        if (Contains(HistoryTabMonth, x, y))
        {
            return PopoverActionId.HistoryTabMonth;
        }

        if (Contains(HistoryTabOutages, x, y))
        {
            return PopoverActionId.HistoryTabOutages;
        }

        return PopoverActionId.None;
    }

    public static bool Contains(ViewRect rect, int x, int y)
    {
        return x >= rect.Left && x < rect.Right && y >= rect.Top && y < rect.Bottom;
    }
}
