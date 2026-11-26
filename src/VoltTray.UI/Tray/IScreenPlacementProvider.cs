using VoltTray.UI.Models;

namespace VoltTray.UI.Tray;

public interface IScreenPlacementProvider
{
    (ViewRect WorkArea, TaskbarEdge Edge) GetWorkAreaForAnchor(ViewRect anchorRect);
}
