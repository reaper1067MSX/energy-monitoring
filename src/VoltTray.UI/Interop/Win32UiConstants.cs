namespace VoltTray.UI.Interop;

internal static class Win32UiConstants
{
    public const uint TrayIconId = 1001;
    public const uint WmTrayIcon = 0x0400 + 101;
    public const uint WmPillRefresh = 0x0400 + 102;
    public const uint WmPillHide = 0x0400 + 103;
    public const string TaskbarCreatedMessageName = "TaskbarCreated";
    public const string TrayHostWindowClass = "VoltTray_TrayHostWindow";
    public const string TrayHostWindowTitle = "VoltTray Tray Host";
    public const string PopoverWindowClass = "VoltTray_OptionCPopover";
    public const string TaskbarPillWindowClass = "VoltTray_TaskbarPill";
    public const string TaskbarPillWindowTitle = "VoltTray Taskbar Pill";
    public const string ShellTrayWndClass = "Shell_TrayWnd";
    public const string TrayNotifyWndClass = "TrayNotifyWnd";
    public const string StartButtonClass = "Start";
    public const string ReBarWindowClass = "ReBarWindow32";
    public const string TaskSwWindowClass = "MSTaskSwWClass";
    public const string Win11CompositionBridgeClass = "Windows.UI.Composition.DesktopWindowContentBridge";

    public const int Dpi100 = 96;
    public const int Dpi125 = 120;
    public const int Dpi150 = 144;
    public const int Dpi200 = 192;

    public const ushort ColorPlanesSingle = 1;
    public const ushort BitsPerPixel32 = 32;

    public const uint PillHitTestAlpha = 0x01000000;
    public const int PillDefaultWidthPx = 92;
    public const int PillDefaultHeightPx = 34;
    public const int PillDefaultRightMarginFallbackPx = 220;
    public const int PillEdgeGapPx = 6;

    public const uint MenuCmdOpen = 1001;
    public const uint MenuCmdToggleUsb = 1002;
    public const uint MenuCmdBatteryTest = 1003;
    public const uint MenuCmdToggleBeeper = 1004;
    public const uint MenuCmdExit = 1005;
    public const uint MenuCmdToggleTheme = 1006;
}
