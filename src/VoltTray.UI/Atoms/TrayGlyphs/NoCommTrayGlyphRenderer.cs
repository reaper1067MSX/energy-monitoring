using VoltTray.UI.Atoms;
using VoltTray.Core.Telemetry;
using VoltTray.UI.Models;

namespace VoltTray.UI.Atoms.TrayGlyphs;

internal sealed class NoCommTrayGlyphRenderer : ITrayStateGlyphRenderer
{
    public IReadOnlyCollection<UpsOperationalState> SupportedStates { get; } = [UpsOperationalState.CommunicationsLost];
    public bool UsesDashedOutline => true;

    public void Render(PixelBuffer buffer, ViewRect bodyBounds, ViewRect innerBounds, int batteryPct, uint accentColor)
    {
        buffer.DrawDiagonalLine(bodyBounds.Left, bodyBounds.Bottom, bodyBounds.Right, bodyBounds.Top, accentColor);
    }
}
