using VoltTray.UI.Atoms;
using VoltTray.Core.Telemetry;
using VoltTray.UI.Models;

namespace VoltTray.UI.Atoms.TrayGlyphs;

internal sealed class BatteryLevelTrayGlyphRenderer : ITrayStateGlyphRenderer
{
    public IReadOnlyCollection<UpsOperationalState> SupportedStates { get; } =
    [
        UpsOperationalState.OnBattery,
        UpsOperationalState.BatteryLow
    ];

    public bool UsesDashedOutline => false;

    public void Render(PixelBuffer buffer, ViewRect bodyBounds, ViewRect innerBounds, int batteryPct, uint accentColor)
    {
        buffer.FillBatteryBar(innerBounds, batteryPct, accentColor);
    }
}
