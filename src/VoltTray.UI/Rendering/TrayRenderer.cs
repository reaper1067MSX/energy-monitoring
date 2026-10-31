using System.Runtime.InteropServices;
using VoltTray.Core.Models;
using VoltTray.Core.Views;
using VoltTray.UI.Interop;

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
    private GdiNative.NOTIFYICONDATAW _nid;
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

    private void InitializeHostWindow()
    {
        try
        {
            _wndProc = TrayWndProc;
            string className = "VoltTray_TrayHostWindow";
            IntPtr hInstance = GdiNative.GetModuleHandle(null);

            var wcx = new GdiNative.WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<GdiNative.WNDCLASSEXW>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = hInstance,
                lpszClassName = className
            };

            GdiNative.RegisterClassEx(ref wcx);

            _hwnd = GdiNative.CreateWindowEx(
                0, className, "VoltTray Tray Host", 0,
                0, 0, 0, 0,
                IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        }
        catch
        {
            // Headless / non-interactive environment fallback
        }
    }

    private IntPtr TrayWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == GdiNative.WM_TRAYICON)
        {
            uint evt = (uint)(lParam.ToInt64() & 0xFFFF);
            if (evt == GdiNative.WM_LBUTTONUP)
            {
                OnTrayIconClicked?.Invoke();
            }
            else if (evt == GdiNative.WM_RBUTTONUP)
            {
                OnContextMenuRequested?.Invoke();
            }
        }
        return GdiNative.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    public void UpdateIcon(UpsOperationalState state, int batteryPct, int dpi = 96)
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
                    _nid = new GdiNative.NOTIFYICONDATAW
                    {
                        cbSize = (uint)Marshal.SizeOf<GdiNative.NOTIFYICONDATAW>(),
                        hWnd = _hwnd,
                        uID = 1001,
                        uFlags = GdiNative.NIF_MESSAGE | GdiNative.NIF_ICON | GdiNative.NIF_TIP,
                        uCallbackMessage = GdiNative.WM_TRAYICON,
                        hIcon = _currentHIcon,
                        szTip = CurrentTooltip ?? "VoltTray: Monitor de Energía"
                    };
                    _iconAdded = GdiNative.Shell_NotifyIcon(GdiNative.NIM_ADD, ref _nid);
                }
                else
                {
                    _nid.uFlags = GdiNative.NIF_ICON;
                    _nid.hIcon = _currentHIcon;
                    GdiNative.Shell_NotifyIcon(GdiNative.NIM_MODIFY, ref _nid);
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
                _nid.uFlags = GdiNative.NIF_TIP;
                _nid.szTip = text.Length > 127 ? text[..127] : text;
                GdiNative.Shell_NotifyIcon(GdiNative.NIM_MODIFY, ref _nid);
            }
        }
    }

    public void Recreate()
    {
        lock (_lock)
        {
            if (_iconAdded)
            {
                GdiNative.Shell_NotifyIcon(GdiNative.NIM_DELETE, ref _nid);
                _iconAdded = false;
            }
            if (_currentHIcon != IntPtr.Zero)
            {
                _nid.uFlags = GdiNative.NIF_MESSAGE | GdiNative.NIF_ICON | GdiNative.NIF_TIP;
                _nid.hIcon = _currentHIcon;
                _iconAdded = GdiNative.Shell_NotifyIcon(GdiNative.NIM_ADD, ref _nid);
            }
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
        var rc = new GdiNative.RECT(1, 1, size - 1, size - 1);
        GdiNative.FillRect(hdcMem, ref rc, hBrush);

        GdiNative.DeleteObject(hBrush);
        GdiNative.SelectObject(hdcMem, hOldBmp);
        GdiNative.DeleteDC(hdcMem);

        var info = new GdiNative.ICONINFO
        {
            fIcon = true,
            hbmColor = hbmColor,
            hbmMask = hbmMask
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
                GdiNative.Shell_NotifyIcon(GdiNative.NIM_DELETE, ref _nid);
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
