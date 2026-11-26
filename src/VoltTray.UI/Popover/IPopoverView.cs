using VoltTray.Core.Telemetry;
using VoltTray.UI.Models;

namespace VoltTray.UI.Popover;

public interface IPopoverView : IDisposable
{
    bool IsVisible { get; }
    UITheme CurrentTheme { get; }

    event Action? OnSelfTestRequested;
    event Action? OnToggleBeeperRequested;

    void Show(UpsTelemetry telemetry, UITheme theme, ViewRect iconRect, TaskbarEdge edge, ViewRect workArea);
    void Hide();
}
