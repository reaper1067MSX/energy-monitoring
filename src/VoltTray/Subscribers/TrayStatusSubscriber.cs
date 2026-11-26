using VoltTray.Core.Events;
using VoltTray.Hosting;
using VoltTray.UI.Presenters;
using VoltTray.UI.Tokens;
using VoltTray.UI.Views;

namespace VoltTray.Subscribers;

/// <summary>Keeps the tray icon and tooltip aligned with telemetry and monitoring status.</summary>
public sealed class TrayStatusSubscriber : IDisposable
{
    private readonly IDisposable _telemetrySubscription;
    private readonly IDisposable _statusSubscription;

    public TrayStatusSubscriber(IEventBus eventBus, ITrayIconView trayIcon, SplitCockpitPresenter presenter)
    {
        ArgumentNullException.ThrowIfNull(eventBus);
        ArgumentNullException.ThrowIfNull(trayIcon);
        ArgumentNullException.ThrowIfNull(presenter);

        _telemetrySubscription = eventBus.Subscribe<TelemetryReceived>(
            received => presenter.UpdateTelemetry(received.Telemetry));
        _statusSubscription = eventBus.Subscribe<MonitoringStatusChanged>(changed =>
        {
            trayIcon.UpdateIcon(changed.State, HostConstants.UnknownBatteryPercentage, DesignTokens.Sizes.DefaultDpi);
            trayIcon.SetTooltip(SplitCockpitPresenter.FormatTooltip(
                changed.State,
                HostConstants.UnknownBatteryPercentage,
                runtimeMinutes: null));
        });
    }

    public void Dispose()
    {
        _telemetrySubscription.Dispose();
        _statusSubscription.Dispose();
    }
}
