using VoltTray.Core.Events;
using VoltTray.Core.Safety;
using VoltTray.Shared;
using VoltTray.UI.Models;

namespace VoltTray;

public interface IHostLifecycle : IDisposable
{
    IEventBus EventBus { get; }
    ShutdownCoordinator ShutdownCoordinator { get; }
    Action? ExitAction { get; set; }

    void Start(int intervalMs = ApplicationDefaults.DefaultPollingIntervalMs);
    void Stop();
    void TogglePopoverFromTray(ViewRect? anchorOverride = null);
    void OpenContextMenuFromTray();
}
