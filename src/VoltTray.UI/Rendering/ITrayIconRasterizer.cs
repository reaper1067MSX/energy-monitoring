using VoltTray.Core.Telemetry;

namespace VoltTray.UI.Rendering;

public interface ITrayIconRasterizer
{
    int ResolvePhysicalSize(int dpi);

    uint[] RenderArgbPixels(UpsOperationalState state, int batteryPct, int size, bool isLightTaskbar);
}
