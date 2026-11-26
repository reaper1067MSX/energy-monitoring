using System.Runtime.InteropServices;
using VoltTray.Core.Telemetry;
using VoltTray.UI.Interop;
using VoltTray.UI.Models;
using VoltTray.UI.Views;

namespace VoltTray.UI.Rendering;

/// <summary>
/// Win32 GDI System Tray Icon renderer with atomic HICON swapping,
/// shell registration via Shell_NotifyIconW, and zero handle leaks.
/// Implements ITrayIconView.
/// </summary>
public sealed class TrayRenderer : ITrayIconView
{
    private IntPtr _currentHIcon = IntPtr.Zero;
    private readonly object _lock = new();
    private bool _isDisposed;

    private IntPtr _hwnd = IntPtr.Zero;
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

    public TrayRenderer()
    {
        InitializeHostWindow();
    }

    private unsafe void InitializeHostWindow()
    {
        try
        {
            _wndProc = TrayWndProc;
            string className = "VoltTray_TrayHostWindow";
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
                0,
                className,
                "VoltTray Tray Host",
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
        return GdiNative.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    public unsafe void UpdateIcon(UpsOperationalState state, int batteryPct, int dpi = 96)
    {
        int size = dpi > 144 ? 32 : (dpi > 120 ? 24 : 16);
        IntPtr hNew = CreateGlyphIcon(state, batteryPct, size);

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
                        Id = 1001,
                        Flags = Win32UiConstants.NifMessage | Win32UiConstants.NifIcon | Win32UiConstants.NifTip,
                        CallbackMessage = Win32UiConstants.WmTrayIcon,
                        IconHandle = _currentHIcon
                    };
                    _nid.SetTip(CurrentTooltip ?? "VoltTray: Monitor de Energía");
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
            if (_currentHIcon != IntPtr.Zero)
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
                Id = 1001,
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

    private static IntPtr CreateGlyphIcon(UpsOperationalState state, int batteryPct, int size)
    {
        IntPtr hdcMem = GdiNative.CreateCompatibleDC(IntPtr.Zero);
        IntPtr hbmColor = GdiNative.CreateBitmap(size, size, 1, 32, IntPtr.Zero);
        IntPtr hbmMask = GdiNative.CreateBitmap(size, size, 1, 1, IntPtr.Zero);

        IntPtr hOldBmp = GdiNative.SelectObject(hdcMem, hbmColor);

        uint color = state switch
        {
            UpsOperationalState.LineNormal => 0x005FCB6C,    // Green (BGR)
            UpsOperationalState.OnBattery => 0x004DB8FF,     // Amber (BGR)
            UpsOperationalState.BatteryLow => 0x004D4DFF,    // Red (BGR)
            UpsOperationalState.ChargingAvr => 0x00FFC47C,   // Light Blue (BGR)
            UpsOperationalState.Fault => 0x00727BFF,         // Coral Red (BGR)
            _ => 0x00A6A09A                                  // Gray (No Comm / Paused)
        };

        IntPtr hBrush = GdiNative.CreateSolidBrush(color);
        var rc = new GdiNative.NativeRect(1, 1, size - 1, size - 1);
        GdiNative.FillRect(hdcMem, ref rc, hBrush);

        GdiNative.DeleteObject(hBrush);
        GdiNative.SelectObject(hdcMem, hOldBmp);
        GdiNative.DeleteDC(hdcMem);

        var info = new GdiNative.IconInfo
        {
            IsIcon = 1,
            ColorBitmap = hbmColor,
            MaskBitmap = hbmMask
        };

        IntPtr hIcon = GdiNative.CreateIconIndirect(ref info);

        GdiNative.DeleteObject(hbmColor);
        GdiNative.DeleteObject(hbmMask);

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
