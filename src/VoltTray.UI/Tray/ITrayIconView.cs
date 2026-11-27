using VoltTray.Core.Telemetry;
using VoltTray.UI.Models;

namespace VoltTray.UI.Tray;

public interface ITrayIconView : IDisposable
{
    event Action? OnTrayIconClicked;
    event Action? OnContextMenuRequested;

    nint WindowHandle => 0;

    void UpdateIcon(UpsOperationalState state, int batteryPct, int dpi);
    void SetTooltip(string text);
    void Recreate();
    ViewRect? TryGetIconRect();
}
