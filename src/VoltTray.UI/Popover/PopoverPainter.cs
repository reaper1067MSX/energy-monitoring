using System.Runtime.InteropServices;
using VoltTray.Core.Telemetry;
using VoltTray.UI.Atoms;
using VoltTray.UI.Interop;
using VoltTray.UI.Models;
using VoltTray.UI.Tokens;
using VoltTray.UI.Tokens.Primitives;

namespace VoltTray.UI.Popover;

/// <summary>
/// Option C Split-Cockpit Matrix Popover painter and Win32 DWMWCP_ROUND window host.
/// Enforces 372 px width, SkiaSharp Split-Cockpit rendering, 350 ms hide/reopen debounce,
/// pointer hit-testing, keyboard navigation, and dynamic telemetry omission when Unavailable.
/// </summary>
public sealed class PopoverPainter : IPopoverView
{
    private readonly Func<DateTimeOffset> _clock;
    private readonly bool _enableNativeWindow;
    private readonly IPopoverSurfaceRenderer _surfaceRenderer;
    private readonly ISvgIconLibrary? _ownedIconLibrary;
    private readonly bool _ownsSurfaceRenderer;
    private DateTimeOffset? _lastHiddenAt;
    private long _lastShownTickMs;
    private IntPtr _hwnd;
    private IntPtr _handCursor;
    private IntPtr _arrowCursor;
    private WndProcDelegate? _wndProc;

    public bool IsVisible { get; private set; }
    public UpsTelemetry? CurrentTelemetry { get; private set; }
    public PopoverViewModel? CurrentViewModel { get; private set; }
    public UITheme CurrentTheme { get; private set; } = UITheme.Dark;
    public ViewRect CurrentIconRect { get; private set; }
    public ViewRect CurrentBounds { get; private set; }
    public int FocusedActionIndex { get; private set; }
    public PopoverActionId HoveredAction { get; private set; }
    public PopoverActionId PressedAction { get; private set; }

    public event Action? OnSelfTestRequested;
    public event Action? OnToggleBeeperRequested;

    public PopoverPainter(
        bool enableNativeWindow = false,
        Func<DateTimeOffset>? clock = null,
        IPopoverSurfaceRenderer? surfaceRenderer = null)
    {
        _enableNativeWindow = enableNativeWindow;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);

        if (surfaceRenderer != null)
        {
            _surfaceRenderer = surfaceRenderer;
            _ownsSurfaceRenderer = false;
        }
        else
        {
            _ownedIconLibrary = new SkiaSvgIconLibrary();
            _surfaceRenderer = new SkiaPopoverSurfaceRenderer(_ownedIconLibrary);
            _ownsSurfaceRenderer = true;
        }
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

        bool wasVisible = IsVisible;
        CurrentTelemetry = telemetry;
        CurrentTheme = theme;
        CurrentIconRect = iconRect;
        CurrentViewModel = PopoverViewModel.FromTelemetry(telemetry);
        CurrentBounds = CalculatePlacement(iconRect, edge, workArea, DesignTokens.Sizes.PopoverCardHeight);
        IsVisible = true;

        if (!wasVisible)
        {
            _lastShownTickMs = Environment.TickCount64;
        }

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
        HoveredAction = PopoverActionId.None;
        PressedAction = PopoverActionId.None;

        if (_hwnd != IntPtr.Zero)
        {
            GdiNative.ShowWindow(_hwnd, Win32UiConstants.SwHide);
        }
    }

    public bool HandlePointerMove(int x, int y)
    {
        if (!IsVisible)
        {
            return false;
        }

        PopoverActionId nextHovered = CreateHitTestLayout().HitTest(x, y);
        if (nextHovered == HoveredAction)
        {
            return false;
        }

        HoveredAction = nextHovered;
        InvalidateNativeWindow();
        return true;
    }

    public bool HandlePointerDown(int x, int y)
    {
        if (!IsVisible)
        {
            return false;
        }

        PopoverActionId hit = CreateHitTestLayout().HitTest(x, y);
        if (hit == PopoverActionId.None)
        {
            return false;
        }

        PressedAction = hit;
        HoveredAction = hit;
        FocusedActionIndex = hit == PopoverActionId.SelfTest ? 0 : 1;
        InvalidateNativeWindow();
        return true;
    }

    public bool HandlePointerUp(int x, int y)
    {
        if (!IsVisible)
        {
            return false;
        }

        PopoverActionId previousPressed = PressedAction;
        PressedAction = PopoverActionId.None;
        PopoverActionId hit = CreateHitTestLayout().HitTest(x, y);

        if (previousPressed != PopoverActionId.None)
        {
            InvalidateNativeWindow();
        }

        if (previousPressed == PopoverActionId.None || hit != previousPressed)
        {
            return false;
        }

        if (hit == PopoverActionId.SelfTest)
        {
            TriggerSelfTest();
            return true;
        }

        if (hit == PopoverActionId.ToggleBeeper)
        {
            TriggerToggleBeeper();
            return true;
        }

        return false;
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
                InvalidateNativeWindow();
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
            GdiNative.UnregisterClass(Win32UiConstants.PopoverWindowClass, hInstance);
        }

        if (_ownsSurfaceRenderer)
        {
            _surfaceRenderer.Dispose();
            _ownedIconLibrary?.Dispose();
        }
    }

    private PopoverHitTestLayout CreateHitTestLayout()
    {
        int width = CurrentBounds.Width > 0 ? CurrentBounds.Width : LayoutTokens.PopoverWidth;
        int height = CurrentBounds.Height > 0 ? CurrentBounds.Height : LayoutTokens.PopoverCardHeight;
        return new PopoverHitTestLayout(width, height);
    }

    private void InvalidateNativeWindow()
    {
        if (_hwnd != IntPtr.Zero)
        {
            GdiNative.InvalidateRect(_hwnd, IntPtr.Zero, false);
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

            int darkMode = CurrentTheme == UITheme.Dark ? 1 : 0;
            _ = GdiNative.DwmSetWindowAttribute(
                _hwnd,
                Win32UiConstants.DwmwaUseImmersiveDarkMode,
                ref darkMode,
                sizeof(int));

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
        _handCursor = GdiNative.LoadCursor(IntPtr.Zero, (IntPtr)Win32UiConstants.IdcHand);
        _arrowCursor = GdiNative.LoadCursor(IntPtr.Zero, (IntPtr)Win32UiConstants.IdcArrow);
        IntPtr hInstance = GdiNative.GetModuleHandle(null);

        fixed (char* classNamePtr = Win32UiConstants.PopoverWindowClass)
        {
            var wcx = new WindowClassExW
            {
                Size = (uint)sizeof(WindowClassExW),
                WindowProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                Instance = hInstance,
                Cursor = _arrowCursor,
                ClassName = classNamePtr
            };

            GdiNative.RegisterClassEx(ref wcx);
        }

        _hwnd = GdiNative.CreateWindowEx(
            Win32UiConstants.WsExToolWindow | Win32UiConstants.WsExTopmost,
            Win32UiConstants.PopoverWindowClass,
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
            case Win32UiConstants.WmEraseBkgnd:
                return (IntPtr)1;
            case Win32UiConstants.WmActivate when (wParam.ToInt64() & 0xFFFF) == Win32UiConstants.WaInactive:
                if (Environment.TickCount64 - _lastShownTickMs >= PopoverLayoutConstants.ShowGracePeriodMs)
                {
                    Hide();
                }
                return IntPtr.Zero;
            case Win32UiConstants.WmMouseMove:
            {
                (int x, int y) = DecodePoint(lParam);
                HandlePointerMove(x, y);
                IntPtr cursor = HoveredAction != PopoverActionId.None ? _handCursor : _arrowCursor;
                if (cursor != IntPtr.Zero)
                {
                    GdiNative.SetCursor(cursor);
                }
                return IntPtr.Zero;
            }
            case Win32UiConstants.WmLButtonDown:
            {
                (int x, int y) = DecodePoint(lParam);
                HandlePointerDown(x, y);
                return IntPtr.Zero;
            }
            case Win32UiConstants.WmLButtonUp:
            {
                (int x, int y) = DecodePoint(lParam);
                HandlePointerUp(x, y);
                return IntPtr.Zero;
            }
            case Win32UiConstants.WmMouseLeave:
                HandlePointerMove(-1, -1);
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

    private static (int X, int Y) DecodePoint(IntPtr lParam)
    {
        long packed = lParam.ToInt64();
        int x = unchecked((short)(packed & 0xFFFF));
        int y = unchecked((short)((packed >> 16) & 0xFFFF));
        return (x, y);
    }

    private unsafe void PaintNativeWindow(IntPtr hWnd)
    {
        IntPtr hdc = GdiNative.BeginPaint(hWnd, out PaintStruct paintStruct);
        try
        {
            if (CurrentViewModel is not PopoverViewModel vm)
            {
                return;
            }

            int width = CurrentBounds.Width > 0 ? CurrentBounds.Width : LayoutTokens.PopoverWidth;
            int height = CurrentBounds.Height > 0 ? CurrentBounds.Height : LayoutTokens.PopoverCardHeight;
            var renderState = new PopoverRenderState(
                vm,
                CurrentTheme,
                width,
                height,
                HoveredAction,
                PressedAction,
                FocusedActionIndex);

            byte[] pixels = _surfaceRenderer.RenderBgra32(renderState);
            BlitBgra32(hdc, width, height, pixels);
        }
        finally
        {
            GdiNative.EndPaint(hWnd, ref paintStruct);
        }
    }

    private static unsafe void BlitBgra32(IntPtr hdcDest, int width, int height, byte[] pixels)
    {
        var bmi = new BitmapInfoHeader
        {
            Size = (uint)sizeof(BitmapInfoHeader),
            Width = width,
            Height = -height,
            Planes = Win32UiConstants.ColorPlanesSingle,
            BitCount = Win32UiConstants.BitsPerPixel32,
            Compression = Win32UiConstants.BiRgb
        };

        IntPtr memDc = GdiNative.CreateCompatibleDC(hdcDest);
        if (memDc == IntPtr.Zero)
        {
            return;
        }

        IntPtr dib = GdiNative.CreateDIBSection(memDc, ref bmi, Win32UiConstants.DibRgbColors, out IntPtr bits, IntPtr.Zero, 0);
        if (dib == IntPtr.Zero || bits == IntPtr.Zero)
        {
            if (dib != IntPtr.Zero)
            {
                GdiNative.DeleteObject(dib);
            }

            GdiNative.DeleteDC(memDc);
            return;
        }

        IntPtr oldBitmap = GdiNative.SelectObject(memDc, dib);
        try
        {
            long expectedBytes = (long)width * height * PopoverLayoutConstants.BgraBytesPerPixel;
            long copyBytes = Math.Min(expectedBytes, pixels.Length);
            fixed (byte* src = pixels)
            {
                Buffer.MemoryCopy(src, (void*)bits, expectedBytes, copyBytes);
            }

            GdiNative.BitBlt(hdcDest, 0, 0, width, height, memDc, 0, 0, Win32UiConstants.SrcCopy);
        }
        finally
        {
            GdiNative.SelectObject(memDc, oldBitmap);
            GdiNative.DeleteObject(dib);
            GdiNative.DeleteDC(memDc);
        }
    }
}
