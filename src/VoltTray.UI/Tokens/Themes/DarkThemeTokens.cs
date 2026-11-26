using VoltTray.Core.Telemetry;

namespace VoltTray.UI.Tokens.Themes;

public static class DarkThemeTokens
{
    public const string DefaultTrayColor = "#9AA0A6";

    public static readonly ThemePalette Palette = new(
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

    public static readonly IReadOnlyDictionary<UpsOperationalState, string> TrayStateColors =
        new Dictionary<UpsOperationalState, string>
        {
            [UpsOperationalState.LineNormal] = "#6CCB5F",
            [UpsOperationalState.OnBattery] = "#FFB84D",
            [UpsOperationalState.BatteryLow] = "#FFB84D",
            [UpsOperationalState.ChargingAvr] = "#7CC4FF",
            [UpsOperationalState.Fault] = "#FF7B72"
        };
}
