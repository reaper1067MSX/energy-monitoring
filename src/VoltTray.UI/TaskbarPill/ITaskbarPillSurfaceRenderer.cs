using VoltTray.Core.Telemetry;

namespace VoltTray.UI.TaskbarPill;

public interface ITaskbarPillSurfaceRenderer
{
    uint[] RenderArgbPixels(UpsTelemetry telemetry, int width, int height, bool isLightTaskbar, int dpi);
}
