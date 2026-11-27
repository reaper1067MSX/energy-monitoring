using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;
using VoltTray.Core.Telemetry;
using VoltTray.UI.Interop;
using VoltTray.UI.Models;
using VoltTray.UI.Tokens.Primitives;
using VoltTray.UI.Tray;

namespace VoltTray.UI.TaskbarPill;

/// <summary>
/// Layered Win32 taskbar pill widget anchored inside Shell_TrayWnd (with HWND_TOPMOST fallback),
/// marshaling UI updates onto the owning HWND thread and handling DPI, fullscreen, and TaskbarCreated.
/// </summary>
internal sealed class TaskbarPillWindow : ITaskbarPillView
{
    private static readonly ConcurrentDictionary<nint, TaskbarPillWindow> Instances = new();

    [ThreadStatic]
    private static TaskbarPillWindow? t_creatingInstance;

    private readonly ITaskbarPillPlacementCalculator _placementCalculator;
    private readonly ITaskbarPillSurfaceRenderer _surfaceRenderer;
    private readonly IFullscreenStateProvider _fullscreenProvider;
    private readonly Func<bool> _isLightTaskbarProvider;
    private readonly bool _enableNativeWindow;
    private readonly object _stateLock = new();

    private HWND _hwnd;
    private UpsTelemetry? _lastTelemetry;
    private bool _isParentedToShellTray;
    private System.Drawing.Point _parentOrigin;
    private bool _isWindowShown;
    private bool _isDisposed;

    public event Action? OnPillLeftClicked;
    public event Action? OnPillRightClicked;

    public bool IsVisible { get; private set; }
    public int CurrentDpi { get; private set; } = LayoutTokens.DefaultDpi;
    public ViewRect CurrentBounds { get; private set; }
    public int TaskbarRecoveryCount { get; private set; }

    public TaskbarPillWindow(
        ITaskbarPillPlacementCalculator? placementCalculator = null,
        ITaskbarPillSurfaceRenderer? surfaceRenderer = null,
        IFullscreenStateProvider? fullscreenProvider = null,
        Func<bool>? isLightTaskbarProvider = null,
        bool enableNativeWindow = false)
    {
        _placementCalculator = placementCalculator ?? new TaskbarPillPlacementCalculator();
        _surfaceRenderer = surfaceRenderer ?? new TaskbarPillSurfaceRenderer(TrayIconRasterizer.CreateDefault());
        _fullscreenProvider = fullscreenProvider ?? new Win32FullscreenStateProvider();
        _isLightTaskbarProvider = isLightTaskbarProvider ?? WindowsThemeDetector.IsLightTaskbar;
        _enableNativeWindow = enableNativeWindow;

        if (_enableNativeWindow)
        {
            InitializeNativeWindow();
        }
    }

    public void Update(UpsTelemetry telemetry)
    {
        ArgumentNullException.ThrowIfNull(telemetry);

        HWND targetHwnd;
        lock (_stateLock)
        {
            if (_isDisposed)
            {
                return;
            }

            _lastTelemetry = telemetry;
            targetHwnd = _hwnd;
        }

        if (_enableNativeWindow && !targetHwnd.IsNull)
        {
            PInvoke.PostMessage(targetHwnd, Win32UiConstants.WmPillRefresh, default, default);
            return;
        }

        ApplyTelemetryOnOwningThread();
    }

    public void Hide()
    {
        HWND targetHwnd;
        lock (_stateLock)
        {
            IsVisible = false;
            targetHwnd = _hwnd;
        }

        if (_enableNativeWindow && !targetHwnd.IsNull)
        {
            PInvoke.PostMessage(targetHwnd, Win32UiConstants.WmPillHide, default, default);
        }
    }

    public void HandleTaskbarCreated()
    {
        lock (_stateLock)
        {
            if (_isDisposed)
            {
                return;
            }

            TaskbarRecoveryCount++;
        }

        if (_enableNativeWindow)
        {
            if (_hwnd.IsNull || !PInvoke.IsWindow(_hwnd))
            {
                _isWindowShown = false;
                InitializeNativeWindow();
            }
            else
            {
                AnchorToShellTray();
            }
        }

        ApplyTelemetryOnOwningThread();
    }

    public bool HandleWindowMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == Win32UiConstants.WmPillRefresh)
        {
            ApplyTelemetryOnOwningThread();
            return true;
        }

        if (msg == Win32UiConstants.WmPillHide)
        {
            HideNativeWindowOnOwningThread();
            return true;
        }

        if (msg == PInvoke.WM_DPICHANGED)
        {
            int newDpi = (int)(wParam.ToInt64() & 0xFFFF);
            if (newDpi > 0)
            {
                lock (_stateLock)
                {
                    CurrentDpi = newDpi;
                }
            }

            ApplyTelemetryOnOwningThread();
            return true;
        }

        if (msg == PInvoke.WM_LBUTTONUP)
        {
            OnPillLeftClicked?.Invoke();
            return true;
        }

        if (msg == PInvoke.WM_RBUTTONUP)
        {
            OnPillRightClicked?.Invoke();
            return true;
        }

        return false;
    }

    public unsafe void Dispose()
    {
        HWND hwndToDestroy;
        lock (_stateLock)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            IsVisible = false;
            _isWindowShown = false;
            hwndToDestroy = _hwnd;
            _hwnd = default;
        }

        if (!hwndToDestroy.IsNull)
        {
            Instances.TryRemove((nint)hwndToDestroy.Value, out _);
            PInvoke.DestroyWindow(hwndToDestroy);
            HMODULE hInstance = PInvoke.GetModuleHandle((PCWSTR)null);
            fixed (char* classNamePtr = Win32UiConstants.TaskbarPillWindowClass)
            {
                PInvoke.UnregisterClass(new PCWSTR(classNamePtr), hInstance);
            }
        }
    }

    private void ApplyTelemetryOnOwningThread()
    {
        UpsTelemetry? telemetry;
        int dpi;
        lock (_stateLock)
        {
            if (_isDisposed || _lastTelemetry == null)
            {
                return;
            }

            telemetry = _lastTelemetry;
            dpi = CurrentDpi;
        }

        if (_fullscreenProvider.IsFullscreenActive())
        {
            HideNativeWindowOnOwningThread();
            return;
        }

        ViewRect bounds = ResolveBounds(dpi);
        lock (_stateLock)
        {
            CurrentBounds = bounds;
            IsVisible = true;
        }

        if (!_enableNativeWindow || _hwnd.IsNull)
        {
            return;
        }

        uint[] pixels = _surfaceRenderer.RenderArgbPixels(
            telemetry,
            bounds.Width,
            bounds.Height,
            _isLightTaskbarProvider(),
            dpi);

        PresentLayeredFrame(bounds, pixels);
    }

    private void HideNativeWindowOnOwningThread()
    {
        lock (_stateLock)
        {
            IsVisible = false;
            _isWindowShown = false;
        }

        if (!_hwnd.IsNull)
        {
            PInvoke.ShowWindow(_hwnd, SHOW_WINDOW_CMD.SW_HIDE);
        }
    }

    private ViewRect ResolveBounds(int dpi)
    {
        double scale = dpi / (double)LayoutTokens.DefaultDpi;
        int scaledWidth = Math.Max(1, (int)Math.Round(Win32UiConstants.PillDefaultWidthPx * scale));
        int scaledHeight = Math.Max(1, (int)Math.Round(Win32UiConstants.PillDefaultHeightPx * scale));
        int scaledFallbackMargin = Math.Max(0, (int)Math.Round(Win32UiConstants.PillDefaultRightMarginFallbackPx * scale));

        TaskbarPillLayoutSnapshot snapshot = _enableNativeWindow
            ? CaptureShellSnapshot(scaledWidth, scaledHeight, scaledFallbackMargin)
            : new TaskbarPillLayoutSnapshot(
                new ViewRect(
                    0,
                    LayoutTokens.FallbackScreenHeightPx - Win32UiConstants.PillDefaultHeightPx,
                    LayoutTokens.FallbackScreenWidthPx,
                    LayoutTokens.FallbackScreenHeightPx),
                TrayNotifyRect: null,
                StartButtonRect: null,
                WidgetRect: null,
                AppListRect: null,
                PillWidth: scaledWidth,
                PillHeight: scaledHeight,
                RightMarginFallbackPx: scaledFallbackMargin);

        return _placementCalculator.Calculate(snapshot);
    }

    private unsafe TaskbarPillLayoutSnapshot CaptureShellSnapshot(int pillWidth, int pillHeight, int fallbackMargin)
    {
        HWND shellTray = FindTopLevelWindow(Win32UiConstants.ShellTrayWndClass);
        ViewRect fallbackTaskbar = new(
            0,
            LayoutTokens.FallbackScreenHeightPx - Win32UiConstants.PillDefaultHeightPx,
            LayoutTokens.FallbackScreenWidthPx,
            LayoutTokens.FallbackScreenHeightPx);

        ViewRect taskbarRect = TryGetRect(shellTray) ?? fallbackTaskbar;
        _parentOrigin = new System.Drawing.Point(taskbarRect.Left, taskbarRect.Top);

        HWND notifyWnd = !shellTray.IsNull ? FindChildWindow(shellTray, Win32UiConstants.TrayNotifyWndClass) : default;
        HWND startWnd = !shellTray.IsNull ? FindChildWindow(shellTray, Win32UiConstants.StartButtonClass) : default;

        ViewRect? appListRect = null;
        if (!IsWindows11Taskbar(shellTray))
        {
            HWND rebarWnd = !shellTray.IsNull ? FindChildWindow(shellTray, Win32UiConstants.ReBarWindowClass) : default;
            HWND taskSwWnd = !rebarWnd.IsNull ? FindChildWindow(rebarWnd, Win32UiConstants.TaskSwWindowClass) : default;
            appListRect = TryGetRect(taskSwWnd);
        }

        return new TaskbarPillLayoutSnapshot(
            taskbarRect,
            TryGetRect(notifyWnd),
            TryGetRect(startWnd),
            WidgetRect: null,
            AppListRect: appListRect,
            PillWidth: pillWidth,
            PillHeight: pillHeight,
            RightMarginFallbackPx: fallbackMargin);
    }

    private static bool IsWindows11Taskbar(HWND shellTray) =>
        !shellTray.IsNull && !FindChildWindow(shellTray, Win32UiConstants.Win11CompositionBridgeClass).IsNull;

    private static unsafe HWND FindTopLevelWindow(string className)
    {
        fixed (char* pClass = className)
        {
            return PInvoke.FindWindow(new PCWSTR(pClass), default);
        }
    }

    private static unsafe HWND FindChildWindow(HWND parent, string className)
    {
        fixed (char* pClass = className)
        {
            return PInvoke.FindWindowEx(parent, default, new PCWSTR(pClass), default);
        }
    }

    private static ViewRect? TryGetRect(HWND hwnd)
    {
        if (hwnd.IsNull || !PInvoke.GetWindowRect(hwnd, out RECT rect))
        {
            return null;
        }

        return new ViewRect(rect.left, rect.top, rect.right, rect.bottom);
    }

    private unsafe void InitializeNativeWindow()
    {
        try
        {
            HMODULE hInstance = PInvoke.GetModuleHandle((PCWSTR)null);

            fixed (char* classNamePtr = Win32UiConstants.TaskbarPillWindowClass)
            fixed (char* titlePtr = Win32UiConstants.TaskbarPillWindowTitle)
            {
                var wcx = new WNDCLASSEXW
                {
                    cbSize = (uint)sizeof(WNDCLASSEXW),
                    lpfnWndProc = new WNDPROC(&StaticPillWndProc),
                    hInstance = new HINSTANCE(hInstance.Value),
                    lpszClassName = new PCWSTR(classNamePtr)
                };

                PInvoke.RegisterClassEx(&wcx);

                HWND shellTray = FindTopLevelWindow(Win32UiConstants.ShellTrayWndClass);
                HWND ownerHwnd = IsWindows11Taskbar(shellTray) ? shellTray : default;

                t_creatingInstance = this;
                try
                {
                    _hwnd = PInvoke.CreateWindowEx(
                        WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_LAYERED,
                        new PCWSTR(classNamePtr),
                        new PCWSTR(titlePtr),
                        WINDOW_STYLE.WS_POPUP,
                        0,
                        0,
                        Win32UiConstants.PillDefaultWidthPx,
                        Win32UiConstants.PillDefaultHeightPx,
                        ownerHwnd,
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
            }

            AnchorToShellTray();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or PlatformNotSupportedException)
        {
            // Headless / non-interactive environment fallback.
        }
    }

    private unsafe void AnchorToShellTray()
    {
        if (_hwnd.IsNull)
        {
            return;
        }

        HWND shellTray = FindTopLevelWindow(Win32UiConstants.ShellTrayWndClass);
        if (!shellTray.IsNull && !IsWindows11Taskbar(shellTray))
        {
            PInvoke.SetParent(_hwnd, shellTray);
            _isParentedToShellTray = PInvoke.GetParent(_hwnd) == shellTray;
            if (_isParentedToShellTray && TryGetRect(shellTray) is ViewRect taskbarRect)
            {
                _parentOrigin = new System.Drawing.Point(taskbarRect.Left, taskbarRect.Top);
                return;
            }
        }

        _isParentedToShellTray = false;
        PInvoke.SetWindowPos(
            _hwnd,
            new HWND((void*)-1),
            0,
            0,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe LRESULT StaticPillWndProc(HWND hWnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        nint key = (nint)hWnd.Value;
        if (!Instances.TryGetValue(key, out TaskbarPillWindow? instance) && t_creatingInstance != null)
        {
            instance = t_creatingInstance;
            Instances[key] = instance;
        }

        if (instance != null && instance.HandleWindowMessage(msg, (IntPtr)(nuint)wParam.Value, lParam.Value))
        {
            return default;
        }

        return PInvoke.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private unsafe void PresentLayeredFrame(ViewRect bounds, uint[] pixels)
    {
        if (_hwnd.IsNull || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        long dibByteCount = (long)bounds.Width * bounds.Height * sizeof(uint);
        if ((long)pixels.Length * sizeof(uint) < dibByteCount)
        {
            return;
        }

        HDC memDc = PInvoke.CreateCompatibleDC(default);
        if (memDc.IsNull)
        {
            return;
        }

        HBITMAP dib = default;
        HGDIOBJ oldBitmap = default;
        try
        {
            var bmi = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)sizeof(BITMAPINFOHEADER),
                    biWidth = bounds.Width,
                    biHeight = -bounds.Height,
                    biPlanes = Win32UiConstants.ColorPlanesSingle,
                    biBitCount = Win32UiConstants.BitsPerPixel32,
                    biCompression = (uint)BI_COMPRESSION.BI_RGB
                }
            };

            dib = PInvoke.CreateDIBSection(memDc, &bmi, DIB_USAGE.DIB_RGB_COLORS, out void* bitsPtr, default, 0);
            if (dib.IsNull || bitsPtr == null)
            {
                return;
            }

            fixed (uint* srcPtr = pixels)
            {
                Buffer.MemoryCopy(srcPtr, bitsPtr, dibByteCount, dibByteCount);
            }

            oldBitmap = PInvoke.SelectObject(memDc, dib);
            int dstX = _isParentedToShellTray ? bounds.Left - _parentOrigin.X : bounds.Left;
            int dstY = _isParentedToShellTray ? bounds.Top - _parentOrigin.Y : bounds.Top;
            var dstPoint = new System.Drawing.Point(dstX, dstY);
            var dstSize = new SIZE(bounds.Width, bounds.Height);
            var srcPoint = new System.Drawing.Point(0, 0);
            var blend = new BLENDFUNCTION
            {
                BlendOp = (byte)PInvoke.AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = byte.MaxValue,
                AlphaFormat = (byte)PInvoke.AC_SRC_ALPHA
            };

            PInvoke.UpdateLayeredWindow(
                _hwnd,
                default,
                dstPoint,
                dstSize,
                memDc,
                srcPoint,
                default,
                blend,
                UPDATE_LAYERED_WINDOW_FLAGS.ULW_ALPHA);

            if (!_isWindowShown)
            {
                PInvoke.ShowWindow(_hwnd, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
                _isWindowShown = true;
            }
        }
        finally
        {
            if (!oldBitmap.IsNull)
            {
                PInvoke.SelectObject(memDc, oldBitmap);
            }

            if (!dib.IsNull)
            {
                PInvoke.DeleteObject(dib);
            }

            PInvoke.DeleteDC(memDc);
        }
    }
}
