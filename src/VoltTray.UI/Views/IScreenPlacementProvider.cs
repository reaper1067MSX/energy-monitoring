using VoltTray.UI.Models;

namespace VoltTray.UI.Views;

public interface IScreenPlacementProvider
{
    (ViewRect WorkArea, TaskbarEdge Edge) GetWorkAreaForAnchor(ViewRect anchorRect);
}
