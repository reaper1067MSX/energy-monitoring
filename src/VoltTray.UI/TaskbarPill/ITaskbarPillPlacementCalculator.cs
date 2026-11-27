using VoltTray.UI.Models;

namespace VoltTray.UI.TaskbarPill;

public interface ITaskbarPillPlacementCalculator
{
    ViewRect Calculate(TaskbarPillLayoutSnapshot snapshot);
}
