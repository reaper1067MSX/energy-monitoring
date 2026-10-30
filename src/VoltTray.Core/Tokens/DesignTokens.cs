using System.Globalization;
using VoltTray.Core.Models;

namespace VoltTray.Core.Tokens;

public static class DesignTokens
{
    public static class Typography
    {
        public const string FontFamily = "Segoe UI Variable, Segoe UI, system-ui, sans-serif";
        public const int Caption = 12;
        public const int Body = 14;
        public const int BodyStrong = 14;
        public const int Subtitle = 18;
        public const int DialogTitle = 20;
        public const int Display = 44;
    }

    public static class Radii
    {
        public const int Card = 8;
        public const int Dialog = 8;
        public const int Button = 6;
        public const int Chip = 12;
        public const int Bar = 4;
        public const int ToggleTrack = 12;
    }

    public static class Sizes
    {
        public const int PopoverWidth = 372;
        public const int DialogWidth = 560;
        public const int MenuWidth = 268;
        public const int ButtonHeight = 44;
        public const int MenuItemHeight = 36;
        public static readonly int[] TrayIconPx = [16, 20, 24, 32];
    }

    public sealed record Palette(
        string Surface,
        string SurfaceInset,
        string Border,
        string BorderStrong,
        string Text,
        string TextMuted,
        string Accent,
        string OnAccent,
        string ButtonSecondary,
        string Hover,
        string Danger,
        string Warning,
        string OnWarning,
        string Ok,
        string OkChipBg,
        string OkChipText,
        string ToggleKnobOn,
        string Taskbar,
        string Icon);

    public static readonly Palette Dark = new(
        Surface: "#2B2B2B",
        SurfaceInset: "#202020",
        Border: "#444444",
        BorderStrong: "#555555",
        Text: "#FFFFFF",
        TextMuted: "#C4C4C4",
        Accent: "#60CDFF",
        OnAccent: "#0A1A22",
        ButtonSecondary: "#363636",
        Hover: "#404040",
        Danger: "#FF9A93",
        Warning: "#FFB84D",
        OnWarning: "#1A1200",
        Ok: "#6CCB5F",
        OkChipBg: "#1D3A22",
        OkChipText: "#7BE070",
        ToggleKnobOn: "#141414",
        Taskbar: "#1C1C1C",
        Icon: "#FFFFFF");

    public static readonly Palette Light = new(
        Surface: "#FAFAFA",
        SurfaceInset: "#EEEEEE",
        Border: "#D0D0D0",
        BorderStrong: "#B5B5B5",
        Text: "#1A1A1A",
        TextMuted: "#4A4A4A",
        Accent: "#005FB8",
        OnAccent: "#FFFFFF",
        ButtonSecondary: "#FFFFFF",
        Hover: "#E3E3E3",
        Danger: "#B3261E",
        Warning: "#9D5D00",
        OnWarning: "#FFFFFF",
        Ok: "#1E7A34",
        OkChipBg: "#E7F6E9",
        OkChipText: "#126425",
        ToggleKnobOn: "#FFFFFF",
        Taskbar: "#F3F3F3",
        Icon: "#1A1A1A");

    public static Palette GetPalette(UITheme theme) => theme == UITheme.Light ? Light : Dark;

    public static string GetTrayStateColor(UpsOperationalState state, bool isLightTaskbar) => (state, isLightTaskbar) switch
    {
        (UpsOperationalState.LineNormal, false) => "#6CCB5F",
        (UpsOperationalState.OnBattery, false) or (UpsOperationalState.BatteryLow, false) => "#FFB84D",
        (UpsOperationalState.ChargingAvr, false) => "#7CC4FF",
        (UpsOperationalState.Fault, false) => "#FF7B72",
        (UpsOperationalState.LineNormal, true) => "#137A2C",
        (UpsOperationalState.OnBattery, true) or (UpsOperationalState.BatteryLow, true) => "#A65600",
        (UpsOperationalState.ChargingAvr, true) => "#005FB8",
        (UpsOperationalState.Fault, true) => "#C42B1C",
        _ => isLightTaskbar ? "#5F6368" : "#9AA0A6"
    };

    public static double CalculateContrastRatio(string foregroundHex, string backgroundHex)
    {
        double lumFg = CalculateLuminance(foregroundHex);
        double lumBg = CalculateLuminance(backgroundHex);
        double l1 = Math.Max(lumFg, lumBg);
        double l2 = Math.Min(lumFg, lumBg);
        return (l1 + 0.05) / (l2 + 0.05);
    }

    private static double CalculateLuminance(string hex)
    {
        string clean = hex.TrimStart('#');
        double r = byte.Parse(clean[..2], NumberStyles.HexNumber) / 255.0;
        double g = byte.Parse(clean.Substring(2, 2), NumberStyles.HexNumber) / 255.0;
        double b = byte.Parse(clean.Substring(4, 2), NumberStyles.HexNumber) / 255.0;

        static double Adjust(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        return 0.2126 * Adjust(r) + 0.7152 * Adjust(g) + 0.0722 * Adjust(b);
    }
}
