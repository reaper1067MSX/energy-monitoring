using VoltTray.Core.Models;

namespace VoltTray.UI.Views;

public interface ITrayIconView : IDisposable
{
    void UpdateIcon(UpsOperationalState state, int batteryPct, int dpi);
    void SetTooltip(string text);
    void Recreate();
}
