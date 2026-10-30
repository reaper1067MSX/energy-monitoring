using VoltTray.Core.Models;

namespace VoltTray.Core.Views;

public interface IPopoverView : IDisposable
{
    bool IsVisible { get; }
    void Show(UpsTelemetry telemetry, UITheme theme, ViewRect iconRect, TaskbarEdge edge, ViewRect workArea);
    void Hide();
}
