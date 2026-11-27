using System.Runtime.InteropServices;
using VoltTray.Core.Telemetry;
using VoltTray.UI.Interop;
using VoltTray.UI.Models;
using VoltTray.UI.Tokens;
using VoltTray.UI.Tokens.Themes;

namespace VoltTray.UI.Popover;

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
    private WndProcDelegate? _wndProc;

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
            GdiNative.ShowWindow(_hwnd, Win32UiConstants.SwHide);
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
        int cardHeight = 320)
    {
        return PopoverPlacementCalculator.Calculate(iconRect, edge, workArea, cardHeight);
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
            IntPtr hInstance = GdiNative.GetModuleHandle(null);
            GdiNative.UnregisterClass("VoltTray_OptionCPopover", hInstance);
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
                (IntPtr)Win32UiConstants.HwndTopmost,
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                Win32UiConstants.SwpNoActivate);
            GdiNative.ShowWindow(_hwnd, Win32UiConstants.SwShow);
            GdiNative.SetForegroundWindow(_hwnd);
            GdiNative.InvalidateRect(_hwnd, IntPtr.Zero, false);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or PlatformNotSupportedException)
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
            var wcx = new WindowClassExW
            {
                Size = (uint)sizeof(WindowClassExW),
                WindowProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                Instance = hInstance,
                ClassName = classNamePtr
            };

            GdiNative.RegisterClassEx(ref wcx);
        }

        _hwnd = GdiNative.CreateWindowEx(
            Win32UiConstants.WsExToolWindow | Win32UiConstants.WsExTopmost,
            className,
            CurrentViewModel?.Title ?? nameof(PopoverPainter),
            Win32UiConstants.WsPopup,
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
            int roundPreference = Win32UiConstants.DwmwcpRound;
            _ = GdiNative.DwmSetWindowAttribute(
                _hwnd,
                Win32UiConstants.DwmwaWindowCornerPreference,
                ref roundPreference,
                sizeof(int));
        }
    }

    private IntPtr PopoverWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case Win32UiConstants.WmActivate when (wParam.ToInt64() & 0xFFFF) == Win32UiConstants.WaInactive:
                Hide();
                return IntPtr.Zero;
            case Win32UiConstants.WmKeyDown:
                HandleKeyDown((int)wParam.ToInt64());
                return IntPtr.Zero;
            case Win32UiConstants.WmPaint:
                PaintNativeWindow(hWnd);
                return IntPtr.Zero;
            default:
                return GdiNative.DefWindowProc(hWnd, msg, wParam, lParam);
        }
    }

    private void PaintNativeWindow(IntPtr hWnd)
    {
        IntPtr hdc = GdiNative.BeginPaint(hWnd, out PaintStruct paintStruct);
        try
        {
            ThemePalette palette = DesignTokens.GetPalette(CurrentTheme);
            IntPtr surfaceBrush = GdiNative.CreateSolidBrush(DesignTokens.ParseBgrColor(palette.Surface));
            NativeRect fullRect = paintStruct.PaintRect;
            GdiNative.FillRect(hdc, ref fullRect, surfaceBrush);
            GdiNative.DeleteObject(surfaceBrush);

            if (CurrentViewModel is not PopoverViewModel vm)
            {
                return;
            }

            GdiNative.SetBkMode(hdc, Win32UiConstants.TransparentBkMode);
            GdiNative.SetTextColor(hdc, DesignTokens.ParseBgrColor(palette.Text));

            var titleRect = new NativeRect(16, 14, CurrentBounds.Width - 16, 40);
            GdiNative.DrawText(hdc, vm.Title, vm.Title.Length, ref titleRect, Win32UiConstants.DtLeft | Win32UiConstants.DtSingleLine);

            GdiNative.SetTextColor(hdc, DesignTokens.ParseBgrColor(palette.TextMuted));
            var subRect = new NativeRect(16, 38, CurrentBounds.Width - 16, 58);
            GdiNative.DrawText(hdc, vm.BankSubtitle, vm.BankSubtitle.Length, ref subRect, Win32UiConstants.DtLeft | Win32UiConstants.DtSingleLine);

            string summary = $"{vm.StatusLabel} · {vm.InputVoltageText} · {vm.RuntimeText}";
            var summaryRect = new NativeRect(16, 76, CurrentBounds.Width - 16, 108);
            GdiNative.SetTextColor(hdc, DesignTokens.ParseBgrColor(palette.Text));
            GdiNative.DrawText(hdc, summary, summary.Length, ref summaryRect, Win32UiConstants.DtLeft | Win32UiConstants.DtSingleLine);
        }
        finally
        {
            GdiNative.EndPaint(hWnd, ref paintStruct);
        }
    }
}
