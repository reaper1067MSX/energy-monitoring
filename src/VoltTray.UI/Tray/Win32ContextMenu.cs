using System.Globalization;
using VoltTray.UI.Interop;
using VoltTray.UI.Tokens;

namespace VoltTray.UI.Tray;

/// <summary>
/// Native Win32 system tray context menu powered by CreatePopupMenu/TrackPopupMenuEx
/// with localized EN/ES labels, bold default Open action, and MockMenuHandler support.
/// </summary>
public sealed class Win32ContextMenu : IContextMenuView
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

    private static ContextMenuAction ShowNativeMenu(ContextMenuState state, nint ownerHandle)
    {
        IntPtr menuHandle = GdiNative.CreatePopupMenu();
        if (menuHandle == IntPtr.Zero)
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

            GdiNative.AppendMenu(menuHandle, Win32UiConstants.MfString, Win32UiConstants.MenuCmdOpen, openLabel);
            GdiNative.SetMenuDefaultItem(menuHandle, Win32UiConstants.MenuCmdOpen, 0);

            GdiNative.AppendMenu(menuHandle, Win32UiConstants.MfSeparator, 0, null);

            uint usbFlags = Win32UiConstants.MfString | (state.IsPaused ? 0u : Win32UiConstants.MfChecked);
            GdiNative.AppendMenu(menuHandle, usbFlags, Win32UiConstants.MenuCmdToggleUsb, usbLabel);

            uint testFlags = Win32UiConstants.MfString | (state.IsTestInProgress ? Win32UiConstants.MfGrayed : 0u);
            GdiNative.AppendMenu(menuHandle, testFlags, Win32UiConstants.MenuCmdBatteryTest, testLabel);

            uint beeperFlags = Win32UiConstants.MfString | (state.IsBeeperMuted ? Win32UiConstants.MfChecked : 0u);
            GdiNative.AppendMenu(menuHandle, beeperFlags, Win32UiConstants.MenuCmdToggleBeeper, beeperLabel);

            GdiNative.AppendMenu(menuHandle, Win32UiConstants.MfSeparator, 0, null);
            GdiNative.AppendMenu(menuHandle, Win32UiConstants.MfString, Win32UiConstants.MenuCmdExit, exitLabel);

            if (!GdiNative.GetCursorPos(out NativePoint cursor))
            {
                cursor = new NativePoint(0, 0);
            }

            if (ownerHandle != 0)
            {
                GdiNative.SetForegroundWindow(ownerHandle);
            }

            uint trackFlags =
                Win32UiConstants.TpmReturnCmd |
                Win32UiConstants.TpmNonotify |
                Win32UiConstants.TpmRightButton |
                Win32UiConstants.TpmBottomAlign;

            int commandId = GdiNative.TrackPopupMenuEx(
                menuHandle,
                trackFlags,
                cursor.X,
                cursor.Y,
                ownerHandle,
                IntPtr.Zero);

            if (ownerHandle != 0)
            {
                GdiNative.PostMessage(ownerHandle, Win32UiConstants.WmNull, IntPtr.Zero, IntPtr.Zero);
            }

            return (uint)commandId switch
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
            GdiNative.DestroyMenu(menuHandle);
        }
    }
}
