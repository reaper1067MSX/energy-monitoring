using System.Runtime.InteropServices;
using VoltTray.Core.Telemetry;
using VoltTray.Shared;
using VoltTray.UI.Atoms;
using VoltTray.UI.Interop;
using VoltTray.UI.Models;
using VoltTray.UI.Tokens.Primitives;

namespace VoltTray.UI.Tray;

/// <summary>
/// Win32 System Tray Icon renderer coordinating Shell_NotifyIconW,
/// atomic HICON swapping, SystemUsesLightTheme detection, and TaskbarCreated recovery.
/// </summary>
public sealed class TrayRenderer : ITrayIconView
{
    private readonly ITrayIconRasterizer _rasterizer;
    private readonly INativeIconFactory _iconFactory;
    private readonly Func<bool> _isLightTaskbarProvider;
    private readonly object _lock = new();
    private IntPtr _currentHIcon = IntPtr.Zero;
    private bool _isDisposed;

    private IntPtr _hwnd = IntPtr.Zero;
    private uint _taskbarCreatedMessage;
    private WndProcDelegate? _wndProc;
    private NotifyIconDataW _nid;
    private bool _iconAdded;

    public event Action? OnTrayIconClicked;
    public event Action? OnContextMenuRequested;
    public event Action? OnTaskbarCreated;

    public IntPtr CurrentHIcon
    {
        get { lock (_lock) return _currentHIcon; }
    }

    public string? CurrentTooltip { get; private set; }
    public uint TaskbarCreatedMessageId => _taskbarCreatedMessage;
    public nint WindowHandle => _hwnd;

    public TrayRenderer(
        ITrayIconRasterizer? rasterizer = null,
        INativeIconFactory? iconFactory = null,
        Func<bool>? isLightTaskbarProvider = null)
    {
        _rasterizer = rasterizer ?? TrayIconRasterizer.CreateDefault();
        _iconFactory = iconFactory ?? new Win32NativeIconFactory();
        _isLightTaskbarProvider = isLightTaskbarProvider ?? WindowsThemeDetector.IsLightTaskbar;
        InitializeHostWindow();
    }

    private unsafe void InitializeHostWindow()
    {
        try
        {
            _taskbarCreatedMessage = GdiNative.RegisterWindowMessage(Win32UiConstants.TaskbarCreatedMessageName);
            _wndProc = TrayWndProc;
            IntPtr hInstance = GdiNative.GetModuleHandle(null);

            fixed (char* classNamePtr = Win32UiConstants.TrayHostWindowClass)
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
                0,
                Win32UiConstants.TrayHostWindowClass,
                Win32UiConstants.TrayHostWindowTitle,
                0,
                0,
                0,
                0,
                0,
                IntPtr.Zero,
                IntPtr.Zero,
                hInstance,
                IntPtr.Zero);
        }
        catch
        {
            // Headless / non-interactive environment fallback
        }
    }

    private IntPtr TrayWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (HandleWindowMessage(msg, lParam))
        {
            return IntPtr.Zero;
        }

        return GdiNative.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    public bool HandleWindowMessage(uint msg, IntPtr lParam)
    {
        if (_taskbarCreatedMessage != 0 && msg == _taskbarCreatedMessage)
        {
            Recreate();
            OnTaskbarCreated?.Invoke();
            return true;
        }

        if (msg == Win32UiConstants.WmTrayIcon)
        {
            uint evt = (uint)(lParam.ToInt64() & 0xFFFF);
            if (evt == Win32UiConstants.WmLButtonUp)
            {
                if (_hwnd != IntPtr.Zero)
                {
                    GdiNative.SetForegroundWindow(_hwnd);
                }

                OnTrayIconClicked?.Invoke();
            }
            else if (evt == Win32UiConstants.WmRButtonUp)
            {
                if (_hwnd != IntPtr.Zero)
                {
                    GdiNative.SetForegroundWindow(_hwnd);
                }

                OnContextMenuRequested?.Invoke();
            }
        }

        return false;
    }

    public unsafe void UpdateIcon(UpsOperationalState state, int batteryPct, int dpi = LayoutTokens.DefaultDpi)
    {
        int size = _rasterizer.ResolvePhysicalSize(dpi);
        uint[] pixels = _rasterizer.RenderArgbPixels(state, batteryPct, size, _isLightTaskbarProvider());
        IntPtr hNew = _iconFactory.CreateFromArgbPixels(pixels, size);

        lock (_lock)
        {
            _iconFactory.DestroyIcon(_currentHIcon);
            _currentHIcon = hNew;

            if (_hwnd != IntPtr.Zero && _currentHIcon != IntPtr.Zero)
            {
                if (!_iconAdded)
                {
                    _nid = new NotifyIconDataW
                    {
                        Size = (uint)sizeof(NotifyIconDataW),
                        WindowHandle = _hwnd,
                        Id = Win32UiConstants.TrayIconId,
                        Flags = Win32UiConstants.NifMessage | Win32UiConstants.NifIcon | Win32UiConstants.NifTip,
                        CallbackMessage = Win32UiConstants.WmTrayIcon,
                        IconHandle = _currentHIcon
                    };
                    _nid.SetTip(CurrentTooltip ?? ApplicationDefaults.DefaultUpsDisplayName);
                    _iconAdded = GdiNative.Shell_NotifyIcon(Win32UiConstants.NimAdd, ref _nid);
                }
                else
                {
                    _nid.Flags = Win32UiConstants.NifIcon;
                    _nid.IconHandle = _currentHIcon;
                    GdiNative.Shell_NotifyIcon(Win32UiConstants.NimModify, ref _nid);
                }
            }
        }
    }

    public void SetTooltip(string text)
    {
        lock (_lock)
        {
            CurrentTooltip = text;
            if (_iconAdded && _hwnd != IntPtr.Zero)
            {
                _nid.Flags = Win32UiConstants.NifTip;
                _nid.SetTip(text);
                GdiNative.Shell_NotifyIcon(Win32UiConstants.NimModify, ref _nid);
            }
        }
    }

    public void Recreate()
    {
        lock (_lock)
        {
            if (_iconAdded)
            {
                GdiNative.Shell_NotifyIcon(Win32UiConstants.NimDelete, ref _nid);
                _iconAdded = false;
            }
            if (_currentHIcon != IntPtr.Zero && _hwnd != IntPtr.Zero)
            {
                _nid.Flags = Win32UiConstants.NifMessage | Win32UiConstants.NifIcon | Win32UiConstants.NifTip;
                _nid.IconHandle = _currentHIcon;
                _iconAdded = GdiNative.Shell_NotifyIcon(Win32UiConstants.NimAdd, ref _nid);
            }
        }
    }

    public unsafe ViewRect? TryGetIconRect()
    {
        lock (_lock)
        {
            if (!_iconAdded || _hwnd == IntPtr.Zero)
            {
                return null;
            }

            var identifier = new NotifyIconIdentifier
            {
                Size = (uint)sizeof(NotifyIconIdentifier),
                WindowHandle = _hwnd,
                Id = Win32UiConstants.TrayIconId,
                GuidItem = Guid.Empty
            };

            int hresult = GdiNative.Shell_NotifyIconGetRect(ref identifier, out NativeRect iconRect);
            if (hresult < 0 || iconRect.Right <= iconRect.Left || iconRect.Bottom <= iconRect.Top)
            {
                return null;
            }

            return new ViewRect(iconRect.Left, iconRect.Top, iconRect.Right, iconRect.Bottom);
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        lock (_lock)
        {
            if (_iconAdded)
            {
                GdiNative.Shell_NotifyIcon(Win32UiConstants.NimDelete, ref _nid);
                _iconAdded = false;
            }
            if (_currentHIcon != IntPtr.Zero)
            {
                _iconFactory.DestroyIcon(_currentHIcon);
                _currentHIcon = IntPtr.Zero;
            }
            if (_hwnd != IntPtr.Zero)
            {
                GdiNative.DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
        }
    }
}
