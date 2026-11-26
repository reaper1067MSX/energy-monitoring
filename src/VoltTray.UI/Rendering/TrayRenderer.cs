using System.Runtime.InteropServices;
using VoltTray.Core.Telemetry;
using VoltTray.Shared;
using VoltTray.UI.Interop;
using VoltTray.UI.Models;
using VoltTray.UI.Tokens.Primitives;
using VoltTray.UI.Views;

namespace VoltTray.UI.Rendering;

/// <summary>
/// Win32 System Tray Icon renderer using 32-bpp ARGB DIB sections,
/// atomic HICON swapping, SystemUsesLightTheme detection, and TaskbarCreated recovery.
/// </summary>
public sealed class TrayRenderer : ITrayIconView
{
    private readonly Func<bool> _isLightTaskbarProvider;
    private readonly object _lock = new();
    private IntPtr _currentHIcon = IntPtr.Zero;
    private bool _isDisposed;

    private IntPtr _hwnd = IntPtr.Zero;
    private uint _taskbarCreatedMessage;
    private GdiNative.WndProcDelegate? _wndProc;
    private GdiNative.NotifyIconDataW _nid;
    private bool _iconAdded;

    public event Action? OnTrayIconClicked;
    public event Action? OnContextMenuRequested;

    public IntPtr CurrentHIcon
    {
        get { lock (_lock) return _currentHIcon; }
    }

    public string? CurrentTooltip { get; private set; }
    public uint TaskbarCreatedMessageId => _taskbarCreatedMessage;

    public TrayRenderer(Func<bool>? isLightTaskbarProvider = null)
    {
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
            return true;
        }

        if (msg == Win32UiConstants.WmTrayIcon)
        {
            uint evt = (uint)(lParam.ToInt64() & 0xFFFF);
            if (evt == Win32UiConstants.WmLButtonUp)
            {
                OnTrayIconClicked?.Invoke();
            }
            else if (evt == Win32UiConstants.WmRButtonUp)
            {
                OnContextMenuRequested?.Invoke();
            }
        }

        return false;
    }

    public unsafe void UpdateIcon(UpsOperationalState state, int batteryPct, int dpi = LayoutTokens.DefaultDpi)
    {
        int size = TrayIconRasterizer.ResolvePhysicalSize(dpi);
        bool isLightTaskbar = _isLightTaskbarProvider();
        IntPtr hNew = CreateArgbIcon(state, batteryPct, size, isLightTaskbar);

        lock (_lock)
        {
            if (_currentHIcon != IntPtr.Zero)
            {
                GdiNative.DestroyIcon(_currentHIcon);
            }
            _currentHIcon = hNew;

            if (_hwnd != IntPtr.Zero && _currentHIcon != IntPtr.Zero)
            {
                if (!_iconAdded)
                {
                    _nid = new GdiNative.NotifyIconDataW
                    {
                        Size = (uint)sizeof(GdiNative.NotifyIconDataW),
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

            var identifier = new GdiNative.NotifyIconIdentifier
            {
                Size = (uint)sizeof(GdiNative.NotifyIconIdentifier),
                WindowHandle = _hwnd,
                Id = Win32UiConstants.TrayIconId,
                GuidItem = Guid.Empty
            };

            int hresult = GdiNative.Shell_NotifyIconGetRect(ref identifier, out GdiNative.NativeRect iconRect);
            if (hresult < 0 || iconRect.Right <= iconRect.Left || iconRect.Bottom <= iconRect.Top)
            {
                return null;
            }

            return new ViewRect(iconRect.Left, iconRect.Top, iconRect.Right, iconRect.Bottom);
        }
    }

    private static unsafe IntPtr CreateArgbIcon(UpsOperationalState state, int batteryPct, int size, bool isLightTaskbar)
    {
        uint[] pixels = TrayIconRasterizer.RenderArgbPixels(state, batteryPct, size, isLightTaskbar);
        var header = new GdiNative.BitmapInfoHeader
        {
            Size = (uint)sizeof(GdiNative.BitmapInfoHeader),
            Width = size,
            Height = -size,
            Planes = Win32UiConstants.ColorPlanesSingle,
            BitCount = Win32UiConstants.BitsPerPixel32,
            Compression = Win32UiConstants.BiRgb
        };

        IntPtr hbmColor = GdiNative.CreateDIBSection(
            IntPtr.Zero,
            ref header,
            Win32UiConstants.DibRgbColors,
            out IntPtr bitsPtr,
            IntPtr.Zero,
            0);
        if (hbmColor == IntPtr.Zero || bitsPtr == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        fixed (uint* srcPtr = pixels)
        {
            Buffer.MemoryCopy(
                srcPtr,
                (void*)bitsPtr,
                (long)pixels.Length * sizeof(uint),
                (long)pixels.Length * sizeof(uint));
        }

        IntPtr hbmMask = GdiNative.CreateBitmap(size, size, Win32UiConstants.ColorPlanesSingle, Win32UiConstants.ColorPlanesSingle, IntPtr.Zero);
        var info = new GdiNative.IconInfo
        {
            IsIcon = 1,
            ColorBitmap = hbmColor,
            MaskBitmap = hbmMask
        };

        IntPtr hIcon = GdiNative.CreateIconIndirect(ref info);
        GdiNative.DeleteObject(hbmColor);
        if (hbmMask != IntPtr.Zero)
        {
            GdiNative.DeleteObject(hbmMask);
        }

        return hIcon;
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
                GdiNative.DestroyIcon(_currentHIcon);
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
