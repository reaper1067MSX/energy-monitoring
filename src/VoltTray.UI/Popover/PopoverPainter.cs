using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.WindowsAndMessaging;
using VoltTray.Core.History;
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
    private static readonly ConcurrentDictionary<nint, PopoverPainter> Instances = new();

    [ThreadStatic]
    private static PopoverPainter? t_creatingInstance;

    private readonly Func<DateTimeOffset> _clock;
    private readonly bool _enableNativeWindow;
    private readonly IPopoverSurfaceRenderer _surfaceRenderer;
    private readonly ISvgIconLibrary? _ownedIconLibrary;
    private readonly bool _ownsSurfaceRenderer;
    private DateTimeOffset? _lastHiddenAt;
    private long _lastShownTickMs;
    private HWND _hwnd;
    private HCURSOR _handCursor;
    private HCURSOR _arrowCursor;

    public bool IsVisible { get; private set; }
    public UpsTelemetry? CurrentTelemetry { get; private set; }
    public PopoverViewModel? CurrentViewModel { get; private set; }
    public UITheme CurrentTheme { get; private set; } = UITheme.Dark;
    public ViewRect CurrentIconRect { get; private set; }
    public ViewRect CurrentBounds { get; private set; }
    public int FocusedActionIndex { get; private set; }
    public PopoverActionId HoveredAction { get; private set; }
    public PopoverActionId PressedAction { get; private set; }
    public HistoryViewTab ActiveHistoryTab { get; private set; } = HistoryViewTab.FiveDays;

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
        => Show(telemetry, theme, iconRect, edge, workArea, history: null);

    public void Show(
        UpsTelemetry telemetry,
        UITheme theme,
        ViewRect iconRect,
        TaskbarEdge edge,
        ViewRect workArea,
        TelemetryHistorySnapshot? history)
    {
        if (IsToggleDebounced())
        {
            return;
        }

        bool wasVisible = IsVisible;
        CurrentTelemetry = telemetry;
        CurrentTheme = theme;
        CurrentIconRect = iconRect;
        CurrentViewModel = PopoverViewModel.FromTelemetry(telemetry, history: history);
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

        if (!_hwnd.IsNull)
        {
            PInvoke.ShowWindow(_hwnd, SHOW_WINDOW_CMD.SW_HIDE);
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

        if (hit is PopoverActionId.HistoryTab5Days or PopoverActionId.HistoryTabMonth or PopoverActionId.HistoryTabOutages)
        {
            ActiveHistoryTab = hit switch
            {
                PopoverActionId.HistoryTabMonth => HistoryViewTab.Month,
                PopoverActionId.HistoryTabOutages => HistoryViewTab.OutageDays,
                _ => HistoryViewTab.FiveDays
            };
            InvalidateNativeWindow();
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

    public unsafe void Dispose()
    {
        Hide();
        if (!_hwnd.IsNull)
        {
            Instances.TryRemove((nint)_hwnd.Value, out _);
            PInvoke.DestroyWindow(_hwnd);
            _hwnd = default;
            HMODULE hInstance = PInvoke.GetModuleHandle((PCWSTR)null);
            fixed (char* classNamePtr = Win32UiConstants.PopoverWindowClass)
            {
                PInvoke.UnregisterClass(new PCWSTR(classNamePtr), hInstance);
            }
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
        if (!_hwnd.IsNull)
        {
            PInvoke.InvalidateRect(_hwnd, (RECT?)null, false);
        }
    }

    private unsafe void ShowOrUpdateNativeWindow(ViewRect bounds)
    {
        try
        {
            EnsureNativeWindowCreated(bounds);
            if (_hwnd.IsNull)
            {
                return;
            }

            int darkMode = CurrentTheme == UITheme.Dark ? 1 : 0;
            _ = PInvoke.DwmSetWindowAttribute(
                _hwnd,
                DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE,
                &darkMode,
                sizeof(int));

            PInvoke.SetWindowPos(
                _hwnd,
                new HWND((void*)-1),
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
            PInvoke.ShowWindow(_hwnd, SHOW_WINDOW_CMD.SW_SHOW);
            PInvoke.SetForegroundWindow(_hwnd);
            PInvoke.InvalidateRect(_hwnd, (RECT?)null, false);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or PlatformNotSupportedException)
        {
            // Headless / non-interactive fallback.
        }
    }

    private unsafe void EnsureNativeWindowCreated(ViewRect bounds)
    {
        if (!_hwnd.IsNull)
        {
            return;
        }

        _handCursor = PInvoke.LoadCursor(default, PInvoke.IDC_HAND);
        _arrowCursor = PInvoke.LoadCursor(default, PInvoke.IDC_ARROW);
        HMODULE hInstance = PInvoke.GetModuleHandle((PCWSTR)null);
        string title = CurrentViewModel?.Title ?? nameof(PopoverPainter);

        fixed (char* classNamePtr = Win32UiConstants.PopoverWindowClass)
        fixed (char* titlePtr = title)
        {
            var wcx = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = new WNDPROC(&StaticPopoverWndProc),
                hInstance = new HINSTANCE(hInstance.Value),
                hCursor = _arrowCursor,
                lpszClassName = new PCWSTR(classNamePtr)
            };

            PInvoke.RegisterClassEx(&wcx);

            t_creatingInstance = this;
            try
            {
                _hwnd = PInvoke.CreateWindowEx(
                    WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_TOPMOST,
                    new PCWSTR(classNamePtr),
                    new PCWSTR(titlePtr),
                    WINDOW_STYLE.WS_POPUP,
                    bounds.Left,
                    bounds.Top,
                    bounds.Width,
                    bounds.Height,
                    default,
                    default,
                    hInstance,
                    null);
            }
            finally
            {
                t_creatingInstance = null;
            }
        }

        if (!_hwnd.IsNull)
        {
            Instances[(nint)_hwnd.Value] = this;
            int roundPreference = (int)DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
            _ = PInvoke.DwmSetWindowAttribute(
                _hwnd,
                DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE,
                &roundPreference,
                sizeof(int));

            if (Environment.OSVersion.Version.Build >= PopoverLayoutConstants.Win11AcrylicBuild)
            {
                var margins = new MARGINS
                {
                    cxLeftWidth = -1,
                    cxRightWidth = -1,
                    cyTopHeight = -1,
                    cyBottomHeight = -1
                };
                _ = PInvoke.DwmExtendFrameIntoClientArea(_hwnd, in margins);

                int backdropType = (int)DWM_SYSTEMBACKDROP_TYPE.DWMSBT_TRANSIENTWINDOW;
                _ = PInvoke.DwmSetWindowAttribute(
                    _hwnd,
                    DWMWINDOWATTRIBUTE.DWMWA_SYSTEMBACKDROP_TYPE,
                    &backdropType,
                    sizeof(int));
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe LRESULT StaticPopoverWndProc(HWND hWnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        nint key = (nint)hWnd.Value;
        if (!Instances.TryGetValue(key, out PopoverPainter? instance) && t_creatingInstance != null)
        {
            instance = t_creatingInstance;
            Instances[key] = instance;
        }

        if (instance != null)
        {
            return instance.PopoverWndProc(hWnd, msg, wParam, lParam);
        }

        return PInvoke.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private LRESULT PopoverWndProc(HWND hWnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        switch (msg)
        {
            case PInvoke.WM_ERASEBKGND:
                return new LRESULT(1);
            case PInvoke.WM_ACTIVATE when (wParam.Value.ToUInt64() & 0xFFFF) == PInvoke.WA_INACTIVE:
                if (Environment.TickCount64 - _lastShownTickMs >= PopoverLayoutConstants.ShowGracePeriodMs)
                {
                    Hide();
                }
                return default;
            case PInvoke.WM_MOUSEMOVE:
            {
                (int x, int y) = DecodePoint(lParam.Value);
                HandlePointerMove(x, y);
                HCURSOR cursor = HoveredAction != PopoverActionId.None ? _handCursor : _arrowCursor;
                if (!cursor.IsNull)
                {
                    PInvoke.SetCursor(cursor);
                }
                return default;
            }
            case PInvoke.WM_LBUTTONDOWN:
            {
                (int x, int y) = DecodePoint(lParam.Value);
                HandlePointerDown(x, y);
                return default;
            }
            case PInvoke.WM_LBUTTONUP:
            {
                (int x, int y) = DecodePoint(lParam.Value);
                HandlePointerUp(x, y);
                return default;
            }
            case PInvoke.WM_MOUSELEAVE:
                HandlePointerMove(-1, -1);
                return default;
            case PInvoke.WM_KEYDOWN:
                HandleKeyDown((int)wParam.Value.ToUInt64());
                return default;
            case PInvoke.WM_PAINT:
                PaintNativeWindow(hWnd);
                return default;
            default:
                return PInvoke.DefWindowProc(hWnd, msg, wParam, lParam);
        }
    }

    private static (int X, int Y) DecodePoint(IntPtr lParam)
    {
        long packed = lParam.ToInt64();
        int x = unchecked((short)(packed & 0xFFFF));
        int y = unchecked((short)((packed >> 16) & 0xFFFF));
        return (x, y);
    }

    private unsafe void PaintNativeWindow(HWND hWnd)
    {
        HDC hdc = PInvoke.BeginPaint(hWnd, out PAINTSTRUCT paintStruct);
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
                FocusedActionIndex,
                UseAcrylicBackdrop: Environment.OSVersion.Version.Build >= PopoverLayoutConstants.Win11AcrylicBuild,
                ActiveHistoryTab: ActiveHistoryTab);

            byte[] pixels = _surfaceRenderer.RenderBgra32(renderState);
            BlitBgra32(hdc, width, height, pixels);
        }
        finally
        {
            PInvoke.EndPaint(hWnd, &paintStruct);
        }
    }

    private static unsafe void BlitBgra32(HDC hdcDest, int width, int height, byte[] pixels)
    {
        var bmi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER),
                biWidth = width,
                biHeight = -height,
                biPlanes = Win32UiConstants.ColorPlanesSingle,
                biBitCount = Win32UiConstants.BitsPerPixel32,
                biCompression = (uint)BI_COMPRESSION.BI_RGB
            }
        };

        HDC memDc = PInvoke.CreateCompatibleDC(hdcDest);
        if (memDc.IsNull)
        {
            return;
        }

        HBITMAP dib = PInvoke.CreateDIBSection(memDc, &bmi, DIB_USAGE.DIB_RGB_COLORS, out void* bits, default, 0);
        if (dib.IsNull || bits == null)
        {
            if (!dib.IsNull)
            {
                PInvoke.DeleteObject(dib);
            }

            PInvoke.DeleteDC(memDc);
            return;
        }

        HGDIOBJ oldBitmap = PInvoke.SelectObject(memDc, dib);
        try
        {
            long expectedBytes = (long)width * height * PopoverLayoutConstants.BgraBytesPerPixel;
            long copyBytes = Math.Min(expectedBytes, pixels.Length);
            fixed (byte* src = pixels)
            {
                Buffer.MemoryCopy(src, bits, expectedBytes, copyBytes);
            }

            PInvoke.BitBlt(hdcDest, 0, 0, width, height, memDc, 0, 0, ROP_CODE.SRCCOPY);
        }
        finally
        {
            PInvoke.SelectObject(memDc, oldBitmap);
            PInvoke.DeleteObject(dib);
            PInvoke.DeleteDC(memDc);
        }
    }
}
