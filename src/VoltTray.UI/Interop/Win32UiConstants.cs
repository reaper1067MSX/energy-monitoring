namespace VoltTray.UI.Interop;

internal static class Win32UiConstants
{
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
    public const uint WmKeyDown = 0x0100;
    public const uint WmLButtonUp = 0x0202;
    public const uint WmRButtonUp = 0x0205;
    public const int WaInactive = 0;

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
}
