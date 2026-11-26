using VoltTray.UI.Atoms;
using VoltTray.Core.Telemetry;
using VoltTray.UI.Models;

namespace VoltTray.UI.Atoms.TrayGlyphs;

public interface ITrayStateGlyphRenderer
{
    IReadOnlyCollection<UpsOperationalState> SupportedStates { get; }

    bool UsesDashedOutline { get; }

    void Render(PixelBuffer buffer, ViewRect bodyBounds, ViewRect innerBounds, int batteryPct, uint accentColor);
}
