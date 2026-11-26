using VoltTray.UI.Atoms;
using VoltTray.Core.Telemetry;
using VoltTray.UI.Models;

namespace VoltTray.UI.Atoms.TrayGlyphs;

public sealed class OnlineTrayGlyphRenderer : ITrayStateGlyphRenderer
{
    public IReadOnlyCollection<UpsOperationalState> SupportedStates { get; } = [UpsOperationalState.LineNormal];
    public bool UsesDashedOutline => false;

    public void Render(PixelBuffer buffer, ViewRect bodyBounds, ViewRect innerBounds, int batteryPct, uint accentColor)
    {
        buffer.FillBatteryBar(innerBounds, batteryPct, accentColor);

        int midX = (innerBounds.Left + innerBounds.Right) / 2;
        int midY = (innerBounds.Top + innerBounds.Bottom) / 2;
        for (int y = innerBounds.Top; y <= innerBounds.Bottom; y++)
        {
            int offset = y <= midY ? (midY - y) / 2 : -((y - midY) / 2);
            buffer.SetPixel(midX + offset, y, 0);
            buffer.SetPixel(midX + offset + 1, y, 0);
        }
    }
}
