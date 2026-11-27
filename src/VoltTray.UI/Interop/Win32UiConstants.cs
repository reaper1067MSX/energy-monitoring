namespace VoltTray.UI.Interop;

internal static class Win32UiConstants
{
    public const int DwmwaUseImmersiveDarkMode = 20;
    public const int DwmwaWindowCornerPreference = 33;
    public const int DwmwcpRound = 2;

    public const uint WsPopup = 0x80000000;
    public const uint WsExToolWindow = 0x00000080;
    public const uint WsExTopmost = 0x00000008;
    public const uint WsExNoActivate = 0x08000000;

    public const int SwHide = 0;
    public const int SwShowNoActivate = 4;
    public const int SwShow = 5;

    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoMove = 0x0002;
    public const uint SwpNoActivate = 0x0010;
    public const uint SwpShowWindow = 0x0040;

    public const int SmCxScreen = 0;
    public const int SmCyScreen = 1;
    public const int SmCxSmIcon = 49;
    public const int SmCySmIcon = 50;
    public const uint MonitorDefaultToNearest = 0x00000002;

    public const uint NimAdd = 0x00000000;
    public const uint NimModify = 0x00000001;
    public const uint NimDelete = 0x00000002;
    public const uint NimSetVersion = 0x00000004;

    public const uint NifMessage = 0x00000001;
    public const uint NifIcon = 0x00000002;
    public const uint NifTip = 0x00000004;

    public const uint WmUser = 0x0400;
    public const uint WmTrayIcon = WmUser + 101;
    public const uint WmActivate = 0x0006;
    public const uint WmPaint = 0x000F;
    public const uint WmEraseBkgnd = 0x0014;
    public const uint WmKeyDown = 0x0100;
    public const uint WmMouseMove = 0x0200;
    public const uint WmLButtonDown = 0x0201;
    public const uint WmLButtonUp = 0x0202;
    public const uint WmRButtonUp = 0x0205;
    public const uint WmMouseLeave = 0x02A3;
    public const int WaInactive = 0;

    public const uint SrcCopy = 0x00CC0020;
    public const int IdcArrow = 32512;
    public const int IdcHand = 32649;
    public const string PopoverWindowClass = "VoltTray_OptionCPopover";

    public const int TransparentBkMode = 1;
    public const int PenStyleSolid = 0;
    public const uint DtLeft = 0x0000;
    public const uint DtCenter = 0x0001;
    public const uint DtRight = 0x0002;
    public const uint DtVCenter = 0x0004;
    public const uint DtSingleLine = 0x0020;

    public const int NotifyTipCapacity = 128;
    public const int NotifyInfoCapacity = 256;
    public const int NotifyInfoTitleCapacity = 64;

    public const uint BiRgb = 0;
    public const uint DibRgbColors = 0;
    public const ushort BitsPerPixel32 = 32;
    public const ushort ColorPlanesSingle = 1;

    public const uint TrayIconId = 1001;
    public const string TaskbarCreatedMessageName = "TaskbarCreated";
    public const string TrayHostWindowClass = "VoltTray_TrayHostWindow";
    public const string TrayHostWindowTitle = "VoltTray Tray Host";

    public const int Dpi100 = 96;
    public const int Dpi125 = 120;
    public const int Dpi150 = 144;
    public const int Dpi200 = 192;

    public const uint WsExLayered = 0x00080000;
    public const byte AcSrcOver = 0;
    public const byte AcSrcAlpha = 1;
    public const uint UlwAlpha = 0x00000002;
    public const uint WmDpiChanged = 0x02E0;
    public const int QunsBusy = 2;
    public const int QunsRunningD3dFullScreen = 3;
    public const int QunsPresentationMode = 4;
    public const uint PillHitTestAlpha = 0x01000000;
    public const int PillDefaultWidthPx = 144;
    public const int PillDefaultHeightPx = 48;
    public const int PillDefaultRightMarginFallbackPx = 220;
    public const int PillEdgeGapPx = 6;
    public const int HwndTopmost = -1;
    public const string ShellTrayWndClass = "Shell_TrayWnd";
    public const string TrayNotifyWndClass = "TrayNotifyWnd";
    public const string StartButtonClass = "Start";
    public const string TaskbarPillWindowClass = "VoltTray_TaskbarPill";
    public const string TaskbarPillWindowTitle = "VoltTray Taskbar Pill";
    public const string ReBarWindowClass = "ReBarWindow32";
    public const string TaskSwWindowClass = "MSTaskSwWClass";
    public const uint WmPillRefresh = WmUser + 102;
    public const uint WmPillHide = WmUser + 103;
    public const byte PillBorderAlpha = 0xCC;
    public const int PillBarHalfHeightPx = 3;
    public const int PillBarHeightPx = 6;
    public const uint MbYesNoCancel = 0x00000003;
    public const uint MbIconQuestion = 0x00000020;
    public const uint MbTopmost = 0x00040000;
    public const int IdYes = 6;
    public const int IdNo = 7;
}
