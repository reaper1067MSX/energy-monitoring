using System.Runtime.InteropServices;
using VoltTray.Core.Telemetry;
using VoltTray.UI.Interop;
using VoltTray.UI.Models;
using VoltTray.UI.Tokens.Primitives;
using VoltTray.UI.Tray;

namespace VoltTray.UI.TaskbarPill;

/// <summary>
/// Layered Win32 taskbar pill widget anchored inside Shell_TrayWnd (with HWND_TOPMOST fallback),
/// supporting UpdateLayeredWindow alpha hit-testing, fullscreen auto-hide, DPI changes, and TaskbarCreated.
/// </summary>
public sealed class TaskbarPillWindow : ITaskbarPillView
{
    private readonly ITaskbarPillPlacementCalculator _placementCalculator;
    private readonly ITaskbarPillSurfaceRenderer _surfaceRenderer;
    private readonly IFullscreenStateProvider _fullscreenProvider;
    private readonly Func<bool> _isLightTaskbarProvider;
    private readonly bool _enableNativeWindow;

    private IntPtr _hwnd;
    private uint _taskbarCreatedMessage;
    private WndProcDelegate? _wndProc;
    private UpsTelemetry? _lastTelemetry;
    private bool _isDisposed;

    public event Action? OnPillLeftClicked;
    public event Action? OnPillRightClicked;

    public bool IsVisible { get; private set; }
    public int CurrentDpi { get; private set; } = LayoutTokens.DefaultDpi;
    public ViewRect CurrentBounds { get; private set; }
    public uint TaskbarCreatedMessageId => _taskbarCreatedMessage;

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
        _lastTelemetry = telemetry;

        if (_fullscreenProvider.IsFullscreenActive())
        {
            Hide();
            return;
        }

        ViewRect bounds = ResolveBounds();
        CurrentBounds = bounds;
        IsVisible = true;

        if (!_enableNativeWindow || _hwnd == IntPtr.Zero)
        {
            return;
        }

        uint[] pixels = _surfaceRenderer.RenderArgbPixels(
            telemetry,
            bounds.Width,
            bounds.Height,
            _isLightTaskbarProvider(),
            CurrentDpi);

        PresentLayeredFrame(bounds, pixels);
    }

    public void Hide()
    {
        IsVisible = false;
        if (_hwnd != IntPtr.Zero)
        {
            GdiNative.ShowWindow(_hwnd, Win32UiConstants.SwHide);
        }
    }

    public bool HandleWindowMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (_taskbarCreatedMessage != 0 && msg == _taskbarCreatedMessage)
        {
            if (_enableNativeWindow)
            {
                AnchorToShellTray();
            }

            if (_lastTelemetry != null)
            {
                Update(_lastTelemetry);
            }

            return true;
        }

        if (msg == Win32UiConstants.WmDpiChanged)
        {
            int newDpi = (int)(wParam.ToInt64() & 0xFFFF);
            if (newDpi > 0)
            {
                CurrentDpi = newDpi;
            }

            if (_lastTelemetry != null)
            {
                Update(_lastTelemetry);
            }

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
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Hide();
        if (_hwnd != IntPtr.Zero)
        {
            GdiNative.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }

    private ViewRect ResolveBounds()
    {
        TaskbarPillLayoutSnapshot snapshot = _enableNativeWindow
            ? CaptureShellSnapshot()
            : new TaskbarPillLayoutSnapshot(
                new ViewRect(0, 1032, LayoutTokens.FallbackScreenWidthPx, LayoutTokens.FallbackScreenHeightPx),
                TrayNotifyRect: null,
                StartButtonRect: null,
                WidgetRect: null,
                AppListRect: null);

        return _placementCalculator.Calculate(snapshot);
    }

    private static TaskbarPillLayoutSnapshot CaptureShellSnapshot()
    {
        IntPtr shellTray = GdiNative.FindWindow(Win32UiConstants.ShellTrayWndClass, null);
        ViewRect taskbarRect = TryGetRect(shellTray) ?? new ViewRect(0, 1032, LayoutTokens.FallbackScreenWidthPx, LayoutTokens.FallbackScreenHeightPx);
        IntPtr notifyWnd = shellTray != IntPtr.Zero ? GdiNative.FindWindowEx(shellTray, IntPtr.Zero, Win32UiConstants.TrayNotifyWndClass, null) : IntPtr.Zero;
        IntPtr startWnd = shellTray != IntPtr.Zero ? GdiNative.FindWindowEx(shellTray, IntPtr.Zero, Win32UiConstants.StartButtonClass, null) : IntPtr.Zero;

        return new TaskbarPillLayoutSnapshot(
            taskbarRect,
            TryGetRect(notifyWnd),
            TryGetRect(startWnd),
            WidgetRect: null,
            AppListRect: null);
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
            _taskbarCreatedMessage = GdiNative.RegisterWindowMessage(Win32UiConstants.TaskbarCreatedMessageName);
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
        catch
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
        if (shellTray == IntPtr.Zero || GdiNative.SetParent(_hwnd, shellTray) == IntPtr.Zero)
        {
            GdiNative.SetWindowPos(
                _hwnd,
                (IntPtr)Win32UiConstants.HwndTopmost,
                0,
                0,
                0,
                0,
                Win32UiConstants.SwpNoMove | Win32UiConstants.SwpNoSize | Win32UiConstants.SwpNoActivate);
        }
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
                Buffer.MemoryCopy(
                    srcPtr,
                    (void*)bitsPtr,
                    (long)pixels.Length * sizeof(uint),
                    (long)pixels.Length * sizeof(uint));
            }

            oldBitmap = GdiNative.SelectObject(memDc, dib);
            var dstPoint = new NativePoint(bounds.Left, bounds.Top);
            var dstSize = new NativeSize(bounds.Width, bounds.Height);
            var srcPoint = new NativePoint(0, 0);
            var blend = new BlendFunction
            {
                BlendOp = Win32UiConstants.AcSrcOver,
                BlendFlags = 0,
                SourceConstantAlpha = byte.MaxValue,
                AlphaFormat = Win32UiConstants.AcSrcAlpha
            };

            GdiNative.ShowWindow(_hwnd, Win32UiConstants.SwShowNoActivate);
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
