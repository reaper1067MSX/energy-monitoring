using VoltTray.Core.Telemetry;
using VoltTray.UI.Models;

namespace VoltTray.UI.Rendering.Glyphs;

public interface ITrayStateGlyphRenderer
{
    IReadOnlyCollection<UpsOperationalState> SupportedStates { get; }

    bool UsesDashedOutline { get; }

    void Render(PixelBuffer buffer, ViewRect bodyBounds, ViewRect innerBounds, int batteryPct, uint accentColor);
}
