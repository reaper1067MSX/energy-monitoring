using System.Runtime.InteropServices;
using VoltTray.Core.Models;
using VoltTray.Core.Views;
using VoltTray.UI.Interop;

namespace VoltTray.UI.Rendering;

/// <summary>
/// Win32 GDI System Tray Icon renderer with atomic HICON swapping and zero handle leaks.
/// Implements ITrayIconView.
/// </summary>
public sealed class TrayRenderer : ITrayIconView
{
    private IntPtr _currentHIcon = IntPtr.Zero;
    private readonly object _lock = new();
    private bool _isDisposed;

    public IntPtr CurrentHIcon
    {
        get
        {
            lock (_lock) return _currentHIcon;
        }
    }

    public string? CurrentTooltip { get; private set; }

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
        }
    }

    public void SetTooltip(string text)
    {
        CurrentTooltip = text;
    }

    public void Recreate()
    {
        // Reinitialization hook when taskbar restarts
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
            if (_currentHIcon != IntPtr.Zero)
            {
                GdiNative.DestroyIcon(_currentHIcon);
                _currentHIcon = IntPtr.Zero;
            }
        }
    }
}
