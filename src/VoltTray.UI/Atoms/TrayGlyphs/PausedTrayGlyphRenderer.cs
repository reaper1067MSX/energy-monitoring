using VoltTray.UI.Atoms;
using VoltTray.Core.Telemetry;
using VoltTray.UI.Models;

namespace VoltTray.UI.Atoms.TrayGlyphs;

internal sealed class PausedTrayGlyphRenderer : ITrayStateGlyphRenderer
{
    public IReadOnlyCollection<UpsOperationalState> SupportedStates { get; } = [UpsOperationalState.Paused];
    public bool UsesDashedOutline => false;

    public void Render(PixelBuffer buffer, ViewRect bodyBounds, ViewRect innerBounds, int batteryPct, uint accentColor)
    {
        int midX = (innerBounds.Left + innerBounds.Right) / 2;
        int leftBarX = Math.Max(innerBounds.Left, midX - 2);
        int rightBarX = Math.Min(innerBounds.Right, midX + 1);

        for (int y = innerBounds.Top; y <= innerBounds.Bottom; y++)
        {
            buffer.SetPixel(leftBarX, y, accentColor);
            buffer.SetPixel(rightBarX, y, accentColor);
            if (innerBounds.Width >= 8)
            {
                buffer.SetPixel(leftBarX + 1, y, accentColor);
                buffer.SetPixel(rightBarX + 1, y, accentColor);
            }
        }
    }
}
