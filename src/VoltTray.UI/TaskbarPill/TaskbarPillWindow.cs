using System.Runtime.InteropServices;
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
public sealed class TaskbarPillWindow : ITaskbarPillView
{
    private readonly ITaskbarPillPlacementCalculator _placementCalculator;
    private readonly ITaskbarPillSurfaceRenderer _surfaceRenderer;
    private readonly IFullscreenStateProvider _fullscreenProvider;
    private readonly Func<bool> _isLightTaskbarProvider;
    private readonly bool _enableNativeWindow;
    private readonly object _stateLock = new();

    private IntPtr _hwnd;
    private WndProcDelegate? _wndProc;
    private UpsTelemetry? _lastTelemetry;
    private bool _isParentedToShellTray;
    private NativePoint _parentOrigin;
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

        IntPtr targetHwnd;
        lock (_stateLock)
        {
            if (_isDisposed)
            {
                return;
            }

            _lastTelemetry = telemetry;
            targetHwnd = _hwnd;
        }

        if (_enableNativeWindow && targetHwnd != IntPtr.Zero)
        {
            GdiNative.PostMessage(targetHwnd, Win32UiConstants.WmPillRefresh, IntPtr.Zero, IntPtr.Zero);
            return;
        }

        ApplyTelemetryOnOwningThread();
    }

    public void Hide()
    {
        IntPtr targetHwnd;
        lock (_stateLock)
        {
            IsVisible = false;
            targetHwnd = _hwnd;
        }

        if (_enableNativeWindow && targetHwnd != IntPtr.Zero)
        {
            GdiNative.PostMessage(targetHwnd, Win32UiConstants.WmPillHide, IntPtr.Zero, IntPtr.Zero);
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
            if (_hwnd == IntPtr.Zero || !GdiNative.IsWindow(_hwnd))
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

        if (msg == Win32UiConstants.WmDpiChanged)
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

        if (msg == Win32UiConstants.WmLButtonUp)
        {
            OnPillLeftClicked?.Invoke();
            return true;
        }

        if (msg == Win32UiConstants.WmRButtonUp)
        {
            OnPillRightClicked?.Invoke();
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        IntPtr hwndToDestroy;
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
            _hwnd = IntPtr.Zero;
        }

        if (hwndToDestroy != IntPtr.Zero)
        {
            GdiNative.DestroyWindow(hwndToDestroy);
            IntPtr hInstance = GdiNative.GetModuleHandle(null);
            GdiNative.UnregisterClass(Win32UiConstants.TaskbarPillWindowClass, hInstance);
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

        if (!_enableNativeWindow || _hwnd == IntPtr.Zero)
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

        if (_hwnd != IntPtr.Zero)
        {
            GdiNative.ShowWindow(_hwnd, Win32UiConstants.SwHide);
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

    private TaskbarPillLayoutSnapshot CaptureShellSnapshot(int pillWidth, int pillHeight, int fallbackMargin)
    {
        IntPtr shellTray = GdiNative.FindWindow(Win32UiConstants.ShellTrayWndClass, null);
        ViewRect fallbackTaskbar = new(
            0,
            LayoutTokens.FallbackScreenHeightPx - Win32UiConstants.PillDefaultHeightPx,
            LayoutTokens.FallbackScreenWidthPx,
            LayoutTokens.FallbackScreenHeightPx);

        ViewRect taskbarRect = TryGetRect(shellTray) ?? fallbackTaskbar;
        _parentOrigin = new NativePoint(taskbarRect.Left, taskbarRect.Top);

        IntPtr notifyWnd = shellTray != IntPtr.Zero ? GdiNative.FindWindowEx(shellTray, IntPtr.Zero, Win32UiConstants.TrayNotifyWndClass, null) : IntPtr.Zero;
        IntPtr startWnd = shellTray != IntPtr.Zero ? GdiNative.FindWindowEx(shellTray, IntPtr.Zero, Win32UiConstants.StartButtonClass, null) : IntPtr.Zero;
        IntPtr rebarWnd = shellTray != IntPtr.Zero ? GdiNative.FindWindowEx(shellTray, IntPtr.Zero, Win32UiConstants.ReBarWindowClass, null) : IntPtr.Zero;
        IntPtr taskSwWnd = rebarWnd != IntPtr.Zero ? GdiNative.FindWindowEx(rebarWnd, IntPtr.Zero, Win32UiConstants.TaskSwWindowClass, null) : IntPtr.Zero;

        return new TaskbarPillLayoutSnapshot(
            taskbarRect,
            TryGetRect(notifyWnd),
            TryGetRect(startWnd),
            WidgetRect: null,
            AppListRect: TryGetRect(taskSwWnd),
            PillWidth: pillWidth,
            PillHeight: pillHeight,
            RightMarginFallbackPx: fallbackMargin);
    }

    private static ViewRect? TryGetRect(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !GdiNative.GetWindowRect(hwnd, out NativeRect rect))
        {
            return null;
        }

        return new ViewRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    private unsafe void InitializeNativeWindow()
    {
        try
        {
            _wndProc = PillWndProc;
            IntPtr hInstance = GdiNative.GetModuleHandle(null);

            fixed (char* classNamePtr = Win32UiConstants.TaskbarPillWindowClass)
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
                Win32UiConstants.WsExToolWindow | Win32UiConstants.WsExNoActivate | Win32UiConstants.WsExLayered,
                Win32UiConstants.TaskbarPillWindowClass,
                Win32UiConstants.TaskbarPillWindowTitle,
                Win32UiConstants.WsPopup,
                0,
                0,
                Win32UiConstants.PillDefaultWidthPx,
                Win32UiConstants.PillDefaultHeightPx,
                IntPtr.Zero,
                IntPtr.Zero,
                hInstance,
                IntPtr.Zero);

            AnchorToShellTray();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or PlatformNotSupportedException)
        {
            // Headless / non-interactive environment fallback.
        }
    }

    private void AnchorToShellTray()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        IntPtr shellTray = GdiNative.FindWindow(Win32UiConstants.ShellTrayWndClass, null);
        if (shellTray != IntPtr.Zero)
        {
            GdiNative.SetParent(_hwnd, shellTray);
            _isParentedToShellTray = GdiNative.GetParent(_hwnd) == shellTray;
            if (_isParentedToShellTray && TryGetRect(shellTray) is ViewRect taskbarRect)
            {
                _parentOrigin = new NativePoint(taskbarRect.Left, taskbarRect.Top);
                return;
            }
        }

        _isParentedToShellTray = false;
        GdiNative.SetWindowPos(
            _hwnd,
            (IntPtr)Win32UiConstants.HwndTopmost,
            0,
            0,
            0,
            0,
            Win32UiConstants.SwpNoMove | Win32UiConstants.SwpNoSize | Win32UiConstants.SwpNoActivate);
    }

    private IntPtr PillWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (HandleWindowMessage(msg, wParam, lParam))
        {
            return IntPtr.Zero;
        }

        return GdiNative.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private unsafe void PresentLayeredFrame(ViewRect bounds, uint[] pixels)
    {
        if (_hwnd == IntPtr.Zero || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        long dibByteCount = (long)bounds.Width * bounds.Height * sizeof(uint);
        if ((long)pixels.Length * sizeof(uint) < dibByteCount)
        {
            return;
        }

        IntPtr memDc = GdiNative.CreateCompatibleDC(IntPtr.Zero);
        if (memDc == IntPtr.Zero)
        {
            return;
        }

        IntPtr dib = IntPtr.Zero;
        IntPtr oldBitmap = IntPtr.Zero;
        try
        {
            var header = new BitmapInfoHeader
            {
                Size = (uint)sizeof(BitmapInfoHeader),
                Width = bounds.Width,
                Height = -bounds.Height,
                Planes = Win32UiConstants.ColorPlanesSingle,
                BitCount = Win32UiConstants.BitsPerPixel32,
                Compression = Win32UiConstants.BiRgb
            };

            dib = GdiNative.CreateDIBSection(memDc, ref header, Win32UiConstants.DibRgbColors, out IntPtr bitsPtr, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || bitsPtr == IntPtr.Zero)
            {
                return;
            }

            fixed (uint* srcPtr = pixels)
            {
                Buffer.MemoryCopy(srcPtr, (void*)bitsPtr, dibByteCount, dibByteCount);
            }

            oldBitmap = GdiNative.SelectObject(memDc, dib);
            int dstX = _isParentedToShellTray ? bounds.Left - _parentOrigin.X : bounds.Left;
            int dstY = _isParentedToShellTray ? bounds.Top - _parentOrigin.Y : bounds.Top;
            var dstPoint = new NativePoint(dstX, dstY);
            var dstSize = new NativeSize(bounds.Width, bounds.Height);
            var srcPoint = new NativePoint(0, 0);
            var blend = new BlendFunction
            {
                BlendOp = Win32UiConstants.AcSrcOver,
                BlendFlags = 0,
                SourceConstantAlpha = byte.MaxValue,
                AlphaFormat = Win32UiConstants.AcSrcAlpha
            };

            GdiNative.UpdateLayeredWindow(
                _hwnd,
                IntPtr.Zero,
                ref dstPoint,
                ref dstSize,
                memDc,
                ref srcPoint,
                0,
                ref blend,
                Win32UiConstants.UlwAlpha);

            if (!_isWindowShown)
            {
                GdiNative.ShowWindow(_hwnd, Win32UiConstants.SwShowNoActivate);
                _isWindowShown = true;
            }
        }
        finally
        {
            if (oldBitmap != IntPtr.Zero)
            {
                GdiNative.SelectObject(memDc, oldBitmap);
            }

            if (dib != IntPtr.Zero)
            {
                GdiNative.DeleteObject(dib);
            }

            GdiNative.DeleteDC(memDc);
        }
    }
}
