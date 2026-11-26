using VoltTray.Core.Models;
using VoltTray.UI.Models;

namespace VoltTray.UI.Views;

public interface IPopoverView : IDisposable
{
    bool IsVisible { get; }
    void Show(UpsTelemetry telemetry, UITheme theme, ViewRect iconRect, TaskbarEdge edge, ViewRect workArea);
    void Hide();
}
