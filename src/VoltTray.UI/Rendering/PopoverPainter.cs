using System.Runtime.InteropServices;
using VoltTray.Core.Telemetry;
using VoltTray.UI.Interop;
using VoltTray.UI.Models;
using VoltTray.UI.Presenters;
using VoltTray.UI.Tokens;
using VoltTray.UI.Views;

namespace VoltTray.UI.Rendering;

/// <summary>
/// Option C Split-Cockpit Matrix Popover painter and Win32 DWMWCP_ROUND window host.
/// Enforces 372 px width, opaque DesignTokens surfaces, 350 ms hide/reopen debounce,
/// keyboard navigation, and dynamic telemetry omission when Unavailable.
/// </summary>
public sealed class PopoverPainter : IPopoverView
{
    private readonly Func<DateTimeOffset> _clock;
    private readonly bool _enableNativeWindow;
    private DateTimeOffset? _lastHiddenAt;
    private IntPtr _hwnd;
    private GdiNative.WndProcDelegate? _wndProc;

    public bool IsVisible { get; private set; }
    public UpsTelemetry? CurrentTelemetry { get; private set; }
    public PopoverViewModel? CurrentViewModel { get; private set; }
    public UITheme CurrentTheme { get; private set; } = UITheme.Dark;
    public ViewRect CurrentIconRect { get; private set; }
    public ViewRect CurrentBounds { get; private set; }
    public int FocusedActionIndex { get; private set; }

    public event Action? OnSelfTestRequested;
    public event Action? OnToggleBeeperRequested;

    public PopoverPainter(bool enableNativeWindow = false, Func<DateTimeOffset>? clock = null)
    {
        _enableNativeWindow = enableNativeWindow;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public bool IsToggleDebounced(DateTimeOffset? now = null)
    {
        if (IsVisible || !_lastHiddenAt.HasValue)
        {
            return false;
        }

        double elapsedMs = ((now ?? _clock()) - _lastHiddenAt.Value).TotalMilliseconds;
        return elapsedMs >= 0 && elapsedMs < DesignTokens.Sizes.PopoverDebounceMs;
    }

    public void Show(UpsTelemetry telemetry, UITheme theme, ViewRect iconRect, TaskbarEdge edge, ViewRect workArea)
    {
        if (IsToggleDebounced())
        {
            return;
        }

        CurrentTelemetry = telemetry;
        CurrentTheme = theme;
        CurrentIconRect = iconRect;
        CurrentViewModel = PopoverViewModel.FromTelemetry(telemetry);
        CurrentBounds = CalculatePlacement(iconRect, edge, workArea, DesignTokens.Sizes.PopoverCardHeight);
        IsVisible = true;

        if (_enableNativeWindow)
        {
            ShowOrUpdateNativeWindow(CurrentBounds);
        }
    }

    public void Hide()
    {
        if (IsVisible)
        {
            _lastHiddenAt = _clock();
        }

        IsVisible = false;
        if (_hwnd != IntPtr.Zero)
        {
            GdiNative.ShowWindow(_hwnd, GdiNative.SW_HIDE);
        }
    }

    public bool HandleKeyDown(int virtualKey)
    {
        if (!IsVisible)
        {
            return false;
        }

        switch (virtualKey)
        {
            case LocalizationConstants.VkEscape:
                Hide();
                return true;
            case LocalizationConstants.VkTab:
                FocusedActionIndex = FocusedActionIndex == 0 ? 1 : 0;
                if (_hwnd != IntPtr.Zero)
                {
                    GdiNative.InvalidateRect(_hwnd, IntPtr.Zero, false);
                }
                return true;
            case LocalizationConstants.VkReturn:
            case LocalizationConstants.VkSpace:
                if (FocusedActionIndex == 0)
                {
                    TriggerSelfTest();
                }
                else
                {
                    TriggerToggleBeeper();
                }
                return true;
            default:
                return false;
        }
    }

    public static ViewRect CalculatePlacement(
        ViewRect iconRect,
        TaskbarEdge edge,
        ViewRect workArea,
        int cardHeight = DesignTokens.Sizes.PopoverCardHeight)
    {
        int popoverWidth = DesignTokens.Sizes.PopoverWidth;
        int margin = DesignTokens.Sizes.PopoverEdgeMarginPx;

        int left;
        int top;

        switch (edge)
        {
            case TaskbarEdge.Top:
                left = iconRect.Left + (iconRect.Width / 2) - (popoverWidth / 2);
                top = iconRect.Bottom + margin;
                break;
            case TaskbarEdge.Left:
                left = iconRect.Right + margin;
                top = iconRect.Top + (iconRect.Height / 2) - (cardHeight / 2);
                break;
            case TaskbarEdge.Right:
                left = iconRect.Left - popoverWidth - margin;
                top = iconRect.Top + (iconRect.Height / 2) - (cardHeight / 2);
                break;
            default:
                left = iconRect.Left + (iconRect.Width / 2) - (popoverWidth / 2);
                top = iconRect.Top - cardHeight - margin;
                break;
        }

        if (left + popoverWidth > workArea.Right - margin)
        {
            left = workArea.Right - popoverWidth - margin;
        }

        if (left < workArea.Left + margin)
        {
            left = workArea.Left + margin;
        }

        if (top + cardHeight > workArea.Bottom - margin)
        {
            top = workArea.Bottom - cardHeight - margin;
        }

        if (top < workArea.Top + margin)
        {
            top = workArea.Top + margin;
        }

        return new ViewRect(left, top, left + popoverWidth, top + cardHeight);
    }

    public void TriggerSelfTest() => OnSelfTestRequested?.Invoke();
    public void TriggerToggleBeeper() => OnToggleBeeperRequested?.Invoke();

    public void Dispose()
    {
        Hide();
        if (_hwnd != IntPtr.Zero)
        {
            GdiNative.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }

    private unsafe void ShowOrUpdateNativeWindow(ViewRect bounds)
    {
        try
        {
            EnsureNativeWindowCreated(bounds);
            if (_hwnd == IntPtr.Zero)
            {
                return;
            }

            GdiNative.SetWindowPos(
                _hwnd,
                IntPtr.Zero,
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                GdiNative.SWP_SHOWWINDOW);
            GdiNative.InvalidateRect(_hwnd, IntPtr.Zero, true);
        }
        catch
        {
            // Headless / non-interactive fallback.
        }
    }

    private unsafe void EnsureNativeWindowCreated(ViewRect bounds)
    {
        if (_hwnd != IntPtr.Zero)
        {
            return;
        }

        _wndProc = PopoverWndProc;
        const string className = "VoltTray_OptionCPopover";
        IntPtr hInstance = GdiNative.GetModuleHandle(null);

        fixed (char* classNamePtr = className)
        {
            var wcx = new GdiNative.WindowClassExW
            {
                Size = (uint)sizeof(GdiNative.WindowClassExW),
                WindowProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                Instance = hInstance,
                ClassName = classNamePtr
            };

            GdiNative.RegisterClassEx(ref wcx);
        }

        _hwnd = GdiNative.CreateWindowEx(
            GdiNative.WS_EX_TOOLWINDOW | GdiNative.WS_EX_TOPMOST,
            className,
            CurrentViewModel?.Title ?? nameof(PopoverPainter),
            GdiNative.WS_POPUP,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            IntPtr.Zero,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);

        if (_hwnd != IntPtr.Zero)
        {
            int roundPreference = GdiNative.DWMWCP_ROUND;
            _ = GdiNative.DwmSetWindowAttribute(
                _hwnd,
                GdiNative.DWMWA_WINDOW_CORNER_PREFERENCE,
                ref roundPreference,
                sizeof(int));
        }
    }

    private IntPtr PopoverWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case GdiNative.WM_ACTIVATE when (wParam.ToInt64() & 0xFFFF) == GdiNative.WA_INACTIVE:
                Hide();
                return IntPtr.Zero;
            case GdiNative.WM_KEYDOWN:
                HandleKeyDown((int)wParam.ToInt64());
                return IntPtr.Zero;
            case GdiNative.WM_PAINT:
                PaintNativeWindow(hWnd);
                return IntPtr.Zero;
            default:
                return GdiNative.DefWindowProc(hWnd, msg, wParam, lParam);
        }
    }

    private void PaintNativeWindow(IntPtr hWnd)
    {
        IntPtr hdc = GdiNative.BeginPaint(hWnd, out GdiNative.PaintStruct paintStruct);
        try
        {
            DesignTokens.Palette palette = DesignTokens.GetPalette(CurrentTheme);
            IntPtr surfaceBrush = GdiNative.CreateSolidBrush(DesignTokens.ParseBgrColor(palette.Surface));
            GdiNative.NativeRect fullRect = paintStruct.PaintRect;
            GdiNative.FillRect(hdc, ref fullRect, surfaceBrush);
            GdiNative.DeleteObject(surfaceBrush);

            if (CurrentViewModel is not PopoverViewModel vm)
            {
                return;
            }

            GdiNative.SetBkMode(hdc, GdiNative.TRANSPARENT);
            GdiNative.SetTextColor(hdc, DesignTokens.ParseBgrColor(palette.Text));

            var titleRect = new GdiNative.NativeRect(16, 14, CurrentBounds.Width - 16, 40);
            GdiNative.DrawText(hdc, vm.Title, vm.Title.Length, ref titleRect, GdiNative.DT_LEFT | GdiNative.DT_SINGLELINE);

            GdiNative.SetTextColor(hdc, DesignTokens.ParseBgrColor(palette.TextMuted));
            var subRect = new GdiNative.NativeRect(16, 38, CurrentBounds.Width - 16, 58);
            GdiNative.DrawText(hdc, vm.BankSubtitle, vm.BankSubtitle.Length, ref subRect, GdiNative.DT_LEFT | GdiNative.DT_SINGLELINE);

            string summary = $"{vm.StatusLabel} · {vm.InputVoltageText} · {vm.RuntimeText}";
            var summaryRect = new GdiNative.NativeRect(16, 76, CurrentBounds.Width - 16, 108);
            GdiNative.SetTextColor(hdc, DesignTokens.ParseBgrColor(palette.Text));
            GdiNative.DrawText(hdc, summary, summary.Length, ref summaryRect, GdiNative.DT_LEFT | GdiNative.DT_SINGLELINE);
        }
        finally
        {
            GdiNative.EndPaint(hWnd, ref paintStruct);
        }
    }
}
