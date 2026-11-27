using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;
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
    private readonly bool _showTrayIcon;
    private readonly object _lock = new();
    private IntPtr _currentHIcon = IntPtr.Zero;
    private bool _isDisposed;

    private static readonly ConcurrentDictionary<nint, TrayRenderer> Instances = new();

    [ThreadStatic]
    private static TrayRenderer? t_creatingInstance;

    private HWND _hwnd;
    private uint _taskbarCreatedMessage;
    private NOTIFYICONDATAW _nid;
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
    public unsafe nint WindowHandle => (nint)_hwnd.Value;

    public TrayRenderer(
        ITrayIconRasterizer? rasterizer = null,
        INativeIconFactory? iconFactory = null,
        Func<bool>? isLightTaskbarProvider = null,
        bool showTrayIcon = true)
    {
        _rasterizer = rasterizer ?? TrayIconRasterizer.CreateDefault();
        _iconFactory = iconFactory ?? new Win32NativeIconFactory();
        _isLightTaskbarProvider = isLightTaskbarProvider ?? WindowsThemeDetector.IsLightTaskbar;
        _showTrayIcon = showTrayIcon;
        InitializeHostWindow();
    }

    private unsafe void InitializeHostWindow()
    {
        try
        {
            fixed (char* msgNamePtr = Win32UiConstants.TaskbarCreatedMessageName)
            {
                _taskbarCreatedMessage = PInvoke.RegisterWindowMessage(new PCWSTR(msgNamePtr));
            }

            HMODULE hInstance = PInvoke.GetModuleHandle((PCWSTR)null);

            fixed (char* classNamePtr = Win32UiConstants.TrayHostWindowClass)
            fixed (char* windowTitlePtr = Win32UiConstants.TrayHostWindowTitle)
            {
                var wcx = new WNDCLASSEXW
                {
                    cbSize = (uint)sizeof(WNDCLASSEXW),
                    lpfnWndProc = new WNDPROC(&StaticTrayWndProc),
                    hInstance = new HINSTANCE(hInstance.Value),
                    lpszClassName = new PCWSTR(classNamePtr)
                };

                PInvoke.RegisterClassEx(&wcx);

                t_creatingInstance = this;
                try
                {
                    _hwnd = PInvoke.CreateWindowEx(
                        0,
                        new PCWSTR(classNamePtr),
                        new PCWSTR(windowTitlePtr),
                        0,
                        0,
                        0,
                        0,
                        0,
                        default,
                        default,
                        hInstance,
                        null);
                }
                finally
                {
                    t_creatingInstance = null;
                }

                if (!_hwnd.IsNull)
                {
                    Instances[(nint)_hwnd.Value] = this;
                }
            }
        }
        catch
        {
            // Headless / non-interactive environment fallback
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe LRESULT StaticTrayWndProc(HWND hWnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        nint key = (nint)hWnd.Value;
        if (!Instances.TryGetValue(key, out TrayRenderer? instance) && t_creatingInstance != null)
        {
            instance = t_creatingInstance;
            Instances[key] = instance;
        }

        if (instance != null && instance.HandleWindowMessage(msg, lParam.Value))
        {
            return default;
        }

        return PInvoke.DefWindowProc(hWnd, msg, wParam, lParam);
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
            if (evt == PInvoke.WM_LBUTTONUP)
            {
                if (!_hwnd.IsNull)
                {
                    PInvoke.SetForegroundWindow(_hwnd);
                }

                OnTrayIconClicked?.Invoke();
            }
            else if (evt == PInvoke.WM_RBUTTONUP)
            {
                if (!_hwnd.IsNull)
                {
                    PInvoke.SetForegroundWindow(_hwnd);
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

            if (_showTrayIcon && !_hwnd.IsNull && _currentHIcon != IntPtr.Zero)
            {
                if (!_iconAdded)
                {
                    _nid = new NOTIFYICONDATAW
                    {
                        cbSize = (uint)sizeof(NOTIFYICONDATAW),
                        hWnd = _hwnd,
                        uID = Win32UiConstants.TrayIconId,
                        uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON | NOTIFY_ICON_DATA_FLAGS.NIF_TIP,
                        uCallbackMessage = Win32UiConstants.WmTrayIcon,
                        hIcon = new HICON(_currentHIcon)
                    };
                    SetTipText(CurrentTooltip ?? ApplicationDefaults.DefaultUpsDisplayName);
                    fixed (NOTIFYICONDATAW* pNid = &_nid)
                    {
                        _iconAdded = PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_ADD, pNid);
                    }
                }
                else
                {
                    _nid.uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_ICON;
                    _nid.hIcon = new HICON(_currentHIcon);
                    fixed (NOTIFYICONDATAW* pNid = &_nid)
                    {
                        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, pNid);
                    }
                }
            }
        }
    }

    public unsafe void SetTooltip(string text)
    {
        lock (_lock)
        {
            CurrentTooltip = text;
            if (_iconAdded && !_hwnd.IsNull)
            {
                _nid.uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_TIP;
                SetTipText(text);
                fixed (NOTIFYICONDATAW* pNid = &_nid)
                {
                    PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, pNid);
                }
            }
        }
    }

    public unsafe void Recreate()
    {
        lock (_lock)
        {
            if (_iconAdded)
            {
                fixed (NOTIFYICONDATAW* pNid = &_nid)
                {
                    PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, pNid);
                }
                _iconAdded = false;
            }
            if (_currentHIcon != IntPtr.Zero && !_hwnd.IsNull)
            {
                _nid.uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON | NOTIFY_ICON_DATA_FLAGS.NIF_TIP;
                _nid.hIcon = new HICON(_currentHIcon);
                fixed (NOTIFYICONDATAW* pNid = &_nid)
                {
                    _iconAdded = PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_ADD, pNid);
                }
            }
        }
    }

    public unsafe ViewRect? TryGetIconRect()
    {
        lock (_lock)
        {
            if (!_iconAdded || _hwnd.IsNull)
            {
                return null;
            }

            var identifier = new NOTIFYICONIDENTIFIER
            {
                cbSize = (uint)sizeof(NOTIFYICONIDENTIFIER),
                hWnd = _hwnd,
                uID = Win32UiConstants.TrayIconId,
                guidItem = Guid.Empty
            };

            int hresult = PInvoke.Shell_NotifyIconGetRect(in identifier, out RECT iconRect).Value;
            if (hresult < 0 || iconRect.right <= iconRect.left || iconRect.bottom <= iconRect.top)
            {
                return null;
            }

            return new ViewRect(iconRect.left, iconRect.top, iconRect.right, iconRect.bottom);
        }
    }

    public unsafe void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        lock (_lock)
        {
            if (_iconAdded)
            {
                fixed (NOTIFYICONDATAW* pNid = &_nid)
                {
                    PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, pNid);
                }
                _iconAdded = false;
            }
            if (_currentHIcon != IntPtr.Zero)
            {
                _iconFactory.DestroyIcon(_currentHIcon);
                _currentHIcon = IntPtr.Zero;
            }
            if (!_hwnd.IsNull)
            {
                Instances.TryRemove((nint)_hwnd.Value, out _);
                PInvoke.DestroyWindow(_hwnd);
                _hwnd = default;
            }
        }
    }

    private unsafe void SetTipText(string? text)
    {
        string value = text ?? string.Empty;
        fixed (char* tipPtr = _nid.szTip.AsSpan())
        {
            int capacity = _nid.szTip.Length - 1;
            int copyLen = Math.Min(value.Length, capacity);
            for (int i = 0; i < copyLen; i++)
            {
                tipPtr[i] = value[i];
            }
            tipPtr[copyLen] = '\0';
        }
    }
}
