using Microsoft.Win32;
using VoltTray.UI.Models;
using VoltTray.UI.Tokens;

namespace VoltTray.UI.Rendering;

/// <summary>
/// Reads Windows theme preferences cleanly: AppsUseLightTheme for app surfaces
/// (Popover/ExitDialog) and SystemUsesLightTheme for taskbar tray glyphs.
/// </summary>
public static class WindowsThemeDetector
{
    public static UITheme GetAppTheme()
    {
        return ReadPersonalizeFlag(LocalizationConstants.AppsUseLightThemeValue, defaultValue: false)
            ? UITheme.Light
            : UITheme.Dark;
    }

    public static bool IsLightTaskbar()
    {
        return ReadPersonalizeFlag(LocalizationConstants.SystemUsesLightThemeValue, defaultValue: false);
    }

    private static bool ReadPersonalizeFlag(string valueName, bool defaultValue)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(LocalizationConstants.RegistryPersonalizePath);
            if (key?.GetValue(valueName) is int flag)
            {
                return flag != 0;
            }
        }
        catch
        {
            // Fall back safely in headless or restricted environments.
        }

        return defaultValue;
    }
}
