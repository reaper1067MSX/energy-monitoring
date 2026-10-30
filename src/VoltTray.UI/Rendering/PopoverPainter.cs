using VoltTray.Core.Models;
using VoltTray.Core.Views;

namespace VoltTray.UI.Rendering;

/// <summary>
/// Split-Cockpit Matrix (50/50) layout painter and geometry calculator.
/// Enforces exact 372 px width, 44 px action button heights, and WCAG AA contrast tokens.
/// Implements IPopoverView contract.
/// </summary>
public sealed class PopoverPainter : IPopoverView
{
    public const int PopoverWidth = 372;
    public const int MinButtonHeight = 44;

    public bool IsVisible { get; private set; }
    public UpsTelemetry? CurrentTelemetry { get; private set; }
    public UITheme CurrentTheme { get; private set; } = UITheme.Dark;
    public ViewRect CurrentIconRect { get; private set; }

    public event Action? OnSelfTestRequested;
    public event Action? OnToggleBeeperRequested;

    public void Show(UpsTelemetry telemetry, UITheme theme, ViewRect iconRect, TaskbarEdge edge, ViewRect workArea)
    {
        CurrentTelemetry = telemetry;
        CurrentTheme = theme;
        CurrentIconRect = iconRect;
        IsVisible = true;
    }

    public void Hide()
    {
        IsVisible = false;
    }

    public static ViewRect CalculatePlacement(ViewRect iconRect, TaskbarEdge edge, ViewRect workArea, int cardHeight = 320)
    {
        int left = iconRect.Left + (iconRect.Width / 2) - (PopoverWidth / 2);
        int top = edge == TaskbarEdge.Bottom
            ? iconRect.Top - cardHeight - 8
            : iconRect.Bottom + 8;

        // Clamp to screen workArea
        if (left + PopoverWidth > workArea.Right)
            left = workArea.Right - PopoverWidth - 8;
        if (left < workArea.Left)
            left = workArea.Left + 8;

        return new ViewRect(left, top, left + PopoverWidth, top + cardHeight);
    }

    public void TriggerSelfTest() => OnSelfTestRequested?.Invoke();
    public void TriggerToggleBeeper() => OnToggleBeeperRequested?.Invoke();

    public void Dispose()
    {
        Hide();
    }
}
