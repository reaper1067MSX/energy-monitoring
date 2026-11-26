using VoltTray.UI.Atoms;
using VoltTray.Core.Telemetry;
using VoltTray.UI.Models;

namespace VoltTray.UI.Atoms.TrayGlyphs;

public sealed class AvrWaveTrayGlyphRenderer : ITrayStateGlyphRenderer
{
    public IReadOnlyCollection<UpsOperationalState> SupportedStates { get; } = [UpsOperationalState.ChargingAvr];
    public bool UsesDashedOutline => false;

    public void Render(PixelBuffer buffer, ViewRect bodyBounds, ViewRect innerBounds, int batteryPct, uint accentColor)
    {
        int midY = (innerBounds.Top + innerBounds.Bottom) / 2;
        int amplitude = Math.Max(1, innerBounds.Height / 3);
        int span = Math.Max(1, innerBounds.Width);

        for (int x = innerBounds.Left; x <= innerBounds.Right; x++)
        {
            double phase = ((x - innerBounds.Left) / (double)span) * Math.PI * 2.0;
            int y = midY - (int)Math.Round(Math.Sin(phase) * amplitude);
            buffer.SetPixel(x, y, accentColor);
            buffer.SetPixel(x, Math.Min(innerBounds.Bottom, y + 1), accentColor);
        }
    }
}
