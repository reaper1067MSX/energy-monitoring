using VoltTray.Core.Telemetry;
using VoltTray.UI.Models;
using VoltTray.UI.Tokens.Primitives;
using VoltTray.UI.Tokens.Themes;

namespace VoltTray.UI.Tokens;

/// <summary>
/// Unified entry point composing layered UI primitives, theme palettes,
/// and WCAG color utilities.
/// </summary>
public static class DesignTokens
{
    public static class Typography
    {
        public static string FontFamily => TypographyTokens.FontFamily;
        public static int Caption => TypographyTokens.Caption;
        public static int Body => TypographyTokens.Body;
        public static int BodyStrong => TypographyTokens.BodyStrong;
        public static int Subtitle => TypographyTokens.Subtitle;
        public static int DialogTitle => TypographyTokens.DialogTitle;
        public static int Display => TypographyTokens.Display;
    }

    public static class Radii
    {
        public static int Card => LayoutTokens.CardRadius;
        public static int Dialog => LayoutTokens.DialogRadius;
        public static int Button => LayoutTokens.ButtonRadius;
        public static int Chip => LayoutTokens.ChipRadius;
        public static int Bar => LayoutTokens.BarRadius;
        public static int ToggleTrack => LayoutTokens.ToggleTrackRadius;
    }

    public static class Sizes
    {
        public static int DefaultDpi => LayoutTokens.DefaultDpi;
        public static int PopoverWidth => LayoutTokens.PopoverWidth;
        public static int PopoverCardHeight => LayoutTokens.PopoverCardHeight;
        public static int PopoverEdgeMarginPx => LayoutTokens.PopoverEdgeMarginPx;
        public static int PopoverDebounceMs => LayoutTokens.PopoverDebounceMs;
        public static int DialogWidth => LayoutTokens.DialogWidth;
        public static int MenuWidth => LayoutTokens.MenuWidth;
        public static int ButtonHeight => LayoutTokens.ButtonHeight;
        public static int MenuItemHeight => LayoutTokens.MenuItemHeight;
        public static int ExitDialogWidth => LayoutTokens.ExitDialogWidth;
        public static int ExitDialogHeight => LayoutTokens.ExitDialogHeight;
        public static int FallbackScreenWidthPx => LayoutTokens.FallbackScreenWidthPx;
        public static int FallbackScreenHeightPx => LayoutTokens.FallbackScreenHeightPx;
        public static int[] TrayIconPx => LayoutTokens.TrayIconPx;
    }

    public static ThemePalette Dark => DarkThemeTokens.Palette;
    public static ThemePalette Light => LightThemeTokens.Palette;

    public static ThemePalette GetPalette(UITheme theme) => theme == UITheme.Light ? Light : Dark;

    public static string GetTrayStateColor(UpsOperationalState state, bool isLightTaskbar)
    {
        IReadOnlyDictionary<UpsOperationalState, string> map = isLightTaskbar
            ? LightThemeTokens.TrayStateColors
            : DarkThemeTokens.TrayStateColors;

        if (map.TryGetValue(state, out string? color))
        {
            return color;
        }

        return isLightTaskbar ? LightThemeTokens.DefaultTrayColor : DarkThemeTokens.DefaultTrayColor;
    }

    public static uint ParseBgrColor(string hex) => ColorUtilities.ParseBgrColor(hex);

    public static double CalculateContrastRatio(string foregroundHex, string backgroundHex) =>
        ColorUtilities.CalculateContrastRatio(foregroundHex, backgroundHex);
}
