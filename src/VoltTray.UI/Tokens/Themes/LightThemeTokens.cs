using VoltTray.Core.Telemetry;

namespace VoltTray.UI.Tokens.Themes;

public static class LightThemeTokens
{
    public const string DefaultTrayColor = "#5F6368";

    public static readonly ThemePalette Palette = new(
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

    public static readonly IReadOnlyDictionary<UpsOperationalState, string> TrayStateColors =
        new Dictionary<UpsOperationalState, string>
        {
            [UpsOperationalState.LineNormal] = "#137A2C",
            [UpsOperationalState.OnBattery] = "#A65600",
            [UpsOperationalState.BatteryLow] = "#A65600",
            [UpsOperationalState.ChargingAvr] = "#005FB8",
            [UpsOperationalState.Fault] = "#C42B1C"
        };
}
