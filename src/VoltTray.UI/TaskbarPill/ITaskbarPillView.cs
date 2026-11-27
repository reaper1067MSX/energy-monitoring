using VoltTray.Core.Telemetry;
using VoltTray.UI.Models;

namespace VoltTray.UI.TaskbarPill;

public interface ITaskbarPillView : IDisposable
{
    event Action? OnPillLeftClicked;
    event Action? OnPillRightClicked;

    bool IsVisible { get; }
    int CurrentDpi { get; }
    ViewRect CurrentBounds { get; }

    void Update(UpsTelemetry telemetry);
    void Hide();
    void HandleTaskbarCreated();
}
