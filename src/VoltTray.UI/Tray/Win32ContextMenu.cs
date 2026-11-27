using System.Globalization;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using VoltTray.UI.Interop;
using VoltTray.UI.Tokens;

namespace VoltTray.UI.Tray;

/// <summary>
/// Native Win32 system tray context menu powered by CreatePopupMenu/TrackPopupMenuEx
/// with localized EN/ES labels, bold default Open action, and MockMenuHandler support.
/// </summary>
internal sealed class Win32ContextMenu : IContextMenuView
{
    private readonly bool _enableNativeMenu;

    public Func<ContextMenuState, ContextMenuAction>? MockMenuHandler { get; set; }

    public Win32ContextMenu(bool enableNativeMenu = false)
    {
        _enableNativeMenu = enableNativeMenu;
    }

    public ContextMenuAction Show(ContextMenuState state, nint ownerHandle = 0)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (MockMenuHandler != null)
        {
            return MockMenuHandler(state);
        }

        if (!_enableNativeMenu)
        {
            return ContextMenuAction.None;
        }

        return ShowNativeMenu(state, ownerHandle);
    }

    private static unsafe ContextMenuAction ShowNativeMenu(ContextMenuState state, nint ownerHandle)
    {
        HMENU menuHandle = PInvoke.CreatePopupMenu();
        if (menuHandle.IsNull)
        {
            return ContextMenuAction.None;
        }

        try
        {
            bool isSpanish = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
                .Equals(LocalizationConstants.SpanishCulturePrefix, StringComparison.OrdinalIgnoreCase);

            string openLabel = isSpanish ? LocalizationConstants.MenuOpenEs : LocalizationConstants.MenuOpenEn;
            string usbLabel = isSpanish ? LocalizationConstants.MenuUsbMonitoringEs : LocalizationConstants.MenuUsbMonitoringEn;
            string testLabel = isSpanish ? LocalizationConstants.MenuBatteryTestEs : LocalizationConstants.MenuBatteryTestEn;
            string beeperLabel = isSpanish ? LocalizationConstants.MenuMuteAlarmEs : LocalizationConstants.MenuMuteAlarmEn;
            string exitLabel = isSpanish ? LocalizationConstants.MenuExitEs : LocalizationConstants.MenuExitEn;

            AppendStringItem(menuHandle, MENU_ITEM_FLAGS.MF_STRING, Win32UiConstants.MenuCmdOpen, openLabel);
            PInvoke.SetMenuDefaultItem(menuHandle, Win32UiConstants.MenuCmdOpen, 0);

            PInvoke.AppendMenu(menuHandle, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, default);

            MENU_ITEM_FLAGS usbFlags = MENU_ITEM_FLAGS.MF_STRING | (state.IsPaused ? 0 : MENU_ITEM_FLAGS.MF_CHECKED);
            AppendStringItem(menuHandle, usbFlags, Win32UiConstants.MenuCmdToggleUsb, usbLabel);

            MENU_ITEM_FLAGS testFlags = MENU_ITEM_FLAGS.MF_STRING | (state.IsTestInProgress ? MENU_ITEM_FLAGS.MF_GRAYED : 0);
            AppendStringItem(menuHandle, testFlags, Win32UiConstants.MenuCmdBatteryTest, testLabel);

            MENU_ITEM_FLAGS beeperFlags = MENU_ITEM_FLAGS.MF_STRING | (state.IsBeeperMuted ? MENU_ITEM_FLAGS.MF_CHECKED : 0);
            AppendStringItem(menuHandle, beeperFlags, Win32UiConstants.MenuCmdToggleBeeper, beeperLabel);

            PInvoke.AppendMenu(menuHandle, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, default);
            AppendStringItem(menuHandle, MENU_ITEM_FLAGS.MF_STRING, Win32UiConstants.MenuCmdExit, exitLabel);

            if (!PInvoke.GetCursorPos(out System.Drawing.Point cursor))
            {
                cursor = default;
            }

            var owner = new HWND(ownerHandle);
            if (!owner.IsNull)
            {
                PInvoke.SetForegroundWindow(owner);
            }

            TRACK_POPUP_MENU_FLAGS trackFlags =
                TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD |
                TRACK_POPUP_MENU_FLAGS.TPM_NONOTIFY |
                TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON |
                TRACK_POPUP_MENU_FLAGS.TPM_BOTTOMALIGN;

            BOOL commandResult = PInvoke.TrackPopupMenuEx(
                menuHandle,
                (uint)trackFlags,
                cursor.X,
                cursor.Y,
                owner,
                null);

            if (!owner.IsNull)
            {
                PInvoke.PostMessage(owner, PInvoke.WM_NULL, default, default);
            }

            return (uint)commandResult.Value switch
            {
                Win32UiConstants.MenuCmdOpen => ContextMenuAction.Open,
                Win32UiConstants.MenuCmdToggleUsb => ContextMenuAction.ToggleUsbMonitoring,
                Win32UiConstants.MenuCmdBatteryTest => ContextMenuAction.BatteryTest,
                Win32UiConstants.MenuCmdToggleBeeper => ContextMenuAction.ToggleBeeper,
                Win32UiConstants.MenuCmdExit => ContextMenuAction.Exit,
                _ => ContextMenuAction.None
            };
        }
        finally
        {
            PInvoke.DestroyMenu(menuHandle);
        }
    }

    private static unsafe void AppendStringItem(HMENU menuHandle, MENU_ITEM_FLAGS flags, uint commandId, string label)
    {
        fixed (char* pLabel = label)
        {
            PInvoke.AppendMenu(menuHandle, flags, commandId, new PCWSTR(pLabel));
        }
    }
}
