using VoltTray.Core.Telemetry;
using VoltTray.UI.Models;

namespace VoltTray.UI.Rendering.Glyphs;

public sealed class FaultTrayGlyphRenderer : ITrayStateGlyphRenderer
{
    public IReadOnlyCollection<UpsOperationalState> SupportedStates { get; } = [UpsOperationalState.Fault];
    public bool UsesDashedOutline => false;

    public void Render(PixelBuffer buffer, ViewRect bodyBounds, ViewRect innerBounds, int batteryPct, uint accentColor)
    {
        int midX = (innerBounds.Left + innerBounds.Right) / 2;
        int dotY = innerBounds.Bottom;
        int stemBottom = Math.Max(innerBounds.Top, dotY - 2);

        for (int y = innerBounds.Top; y <= stemBottom; y++)
        {
            buffer.SetPixel(midX, y, accentColor);
            buffer.SetPixel(Math.Min(innerBounds.Right, midX + 1), y, accentColor);
        }

        buffer.SetPixel(midX, dotY, accentColor);
        buffer.SetPixel(Math.Min(innerBounds.Right, midX + 1), dotY, accentColor);
    }
}
