using VoltTray.Core.Models;
using VoltTray.UI.Models;

namespace VoltTray.UI.Views;

public interface ITrayIconView : IDisposable
{
    event Action? OnTrayIconClicked;
    event Action? OnContextMenuRequested;

    void UpdateIcon(UpsOperationalState state, int batteryPct, int dpi);
    void SetTooltip(string text);
    void Recreate();
    ViewRect? TryGetIconRect();
}
