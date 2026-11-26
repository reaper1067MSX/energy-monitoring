using VoltTray.Core.Models;
using VoltTray.UI.Models;
using VoltTray.UI.Tokens;

namespace VoltTray.Tests;

public class DesignTokensTests
{
    [Theory]
    [InlineData(UpsOperationalState.LineNormal, false, "#6CCB5F")]
    [InlineData(UpsOperationalState.OnBattery, false, "#FFB84D")]
    [InlineData(UpsOperationalState.ChargingAvr, false, "#7CC4FF")]
    [InlineData(UpsOperationalState.Fault, false, "#FF7B72")]
    [InlineData(UpsOperationalState.CommunicationsLost, false, "#9AA0A6")]
    [InlineData(UpsOperationalState.LineNormal, true, "#137A2C")]
    [InlineData(UpsOperationalState.OnBattery, true, "#A65600")]
    [InlineData(UpsOperationalState.ChargingAvr, true, "#005FB8")]
    [InlineData(UpsOperationalState.Fault, true, "#C42B1C")]
    [InlineData(UpsOperationalState.CommunicationsLost, true, "#5F6368")]
    public void TrayStateColors_ResolveCorrectlyForTaskbarThemes(UpsOperationalState state, bool lightTaskbar, string expectedHex)
    {
        string actualHex = DesignTokens.GetTrayStateColor(state, lightTaskbar);
        Assert.Equal(expectedHex, actualHex);
    }

    [Fact]
    public void AllDefinedColorPairs_MeetWcag21AaContrastRatio()
    {
        var dark = DesignTokens.GetPalette(UITheme.Dark);
        var light = DesignTokens.GetPalette(UITheme.Light);

        // 26 verified normative color pairs
        var colorPairs = new (string Name, string Foreground, string Background)[]
        {
            // Dark theme pairs
            ("Dark: Text on Surface", dark.Text, dark.Surface),
            ("Dark: TextMuted on Surface", dark.TextMuted, dark.Surface),
            ("Dark: OnAccent on Accent", dark.OnAccent, dark.Accent),
            ("Dark: Danger on Surface", dark.Danger, dark.Surface),
            ("Dark: Warning on Surface", dark.Warning, dark.Surface),
            ("Dark: OnWarning on Warning", dark.OnWarning, dark.Warning),
            ("Dark: Ok on Surface", dark.Ok, dark.Surface),
            ("Dark: OkChipText on OkChipBg", dark.OkChipText, dark.OkChipBg),

            // Light theme pairs
            ("Light: Text on Surface", light.Text, light.Surface),
            ("Light: TextMuted on Surface", light.TextMuted, light.Surface),
            ("Light: OnAccent on Accent", light.OnAccent, light.Accent),
            ("Light: Danger on Surface", light.Danger, light.Surface),
            ("Light: Warning on Surface", light.Warning, light.Surface),
            ("Light: OnWarning on Warning", light.OnWarning, light.Warning),
            ("Light: Ok on Surface", light.Ok, light.Surface),
            ("Light: OkChipText on OkChipBg", light.OkChipText, light.OkChipBg),

            // Tray state dark taskbar pairs
            ("TrayDark: Online on Taskbar", DesignTokens.GetTrayStateColor(UpsOperationalState.LineNormal, false), dark.Taskbar),
            ("TrayDark: OnBattery on Taskbar", DesignTokens.GetTrayStateColor(UpsOperationalState.OnBattery, false), dark.Taskbar),
            ("TrayDark: Avr on Taskbar", DesignTokens.GetTrayStateColor(UpsOperationalState.ChargingAvr, false), dark.Taskbar),
            ("TrayDark: Fault on Taskbar", DesignTokens.GetTrayStateColor(UpsOperationalState.Fault, false), dark.Taskbar),
            ("TrayDark: NoComm on Taskbar", DesignTokens.GetTrayStateColor(UpsOperationalState.CommunicationsLost, false), dark.Taskbar),

            // Tray state light taskbar pairs
            ("TrayLight: Online on Taskbar", DesignTokens.GetTrayStateColor(UpsOperationalState.LineNormal, true), light.Taskbar),
            ("TrayLight: OnBattery on Taskbar", DesignTokens.GetTrayStateColor(UpsOperationalState.OnBattery, true), light.Taskbar),
            ("TrayLight: Avr on Taskbar", DesignTokens.GetTrayStateColor(UpsOperationalState.ChargingAvr, true), light.Taskbar),
            ("TrayLight: Fault on Taskbar", DesignTokens.GetTrayStateColor(UpsOperationalState.Fault, true), light.Taskbar),
            ("TrayLight: NoComm on Taskbar", DesignTokens.GetTrayStateColor(UpsOperationalState.CommunicationsLost, true), light.Taskbar),
        };

        Assert.Equal(26, colorPairs.Length);

        foreach (var (name, fg, bg) in colorPairs)
        {
            double ratio = DesignTokens.CalculateContrastRatio(fg, bg);
            Assert.True(ratio >= 4.5, $"Pair '{name}' ({fg} on {bg}) failed WCAG 2.1 AA with ratio {ratio:F2}:1 (expected >= 4.5:1)");
        }
    }
}
