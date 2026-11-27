using Microsoft.Extensions.DependencyInjection;
using VoltTray.Core.Diagnostics;
using VoltTray.Core.Safety;
using VoltTray.Core.Telemetry;
using VoltTray.Infrastructure.DependencyInjection;
using VoltTray.Subscribers;
using VoltTray.UI.DependencyInjection;
using VoltTray.UI.Dialogs;
using VoltTray.UI.Popover;
using VoltTray.UI.TaskbarPill;
using VoltTray.UI.Tray;

namespace VoltTray.Hosting;

/// <summary>
/// Composition root for the interactive tray monitor using Microsoft.Extensions.DependencyInjection.
/// Composes layer DI extensions and resolves strictly through interface contracts.
/// </summary>
public sealed class TrayMonitorBootstrapper : IDisposable
{
    private readonly IUpsDevicePort _rawPort;
    private readonly ServiceProvider _serviceProvider;
    private readonly ITrayIconView _trayIconView;
    private readonly ITaskbarPillView? _taskbarPillView;

    public IHostLifecycle Lifecycle { get; }

    public TrayMonitorBootstrapper(TrayMonitorLaunchOptions options, Func<IUpsDevicePort> upsPortFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(upsPortFactory);

        _rawPort = upsPortFactory();

        var services = new ServiceCollection();
        services.AddVoltTrayInfrastructure(() => _rawPort);
        services.AddVoltTrayUi(options.EnableTaskbarPill);

        services.AddSingleton<IHostLifecycle>(sp => new HostLifecycle(
            sp.GetRequiredService<IUpsDevicePort>(),
            sp.GetRequiredService<ISafetyShutdownPort>(),
            sp.GetRequiredService<INotificationPort>(),
            sp.GetRequiredService<ITrayIconView>(),
            sp.GetRequiredService<IPopoverView>(),
            sp.GetRequiredService<IExitDialogView>(),
            sp.GetRequiredService<IScreenPlacementProvider>(),
            sp.GetRequiredService<ITelemetryDiagnosticSink>(),
            sp.GetRequiredService<IExecutionBoundary>(),
            delegateDiagnosticsToPort: true,
            eventBus: null,
            appThemeProvider: WindowsThemeDetector.GetAppTheme,
            contextMenu: sp.GetRequiredService<IContextMenuView>()));

        if (options.EnableTaskbarPill)
        {
            services.AddSingleton(sp => new TaskbarPillSubscriber(
                sp.GetRequiredService<IHostLifecycle>().EventBus,
                sp.GetRequiredService<ITaskbarPillView>(),
                sp.GetRequiredService<IHostLifecycle>()));
        }

        _serviceProvider = services.BuildServiceProvider();
        Lifecycle = _serviceProvider.GetRequiredService<IHostLifecycle>();
        _trayIconView = _serviceProvider.GetRequiredService<ITrayIconView>();

        if (options.IsDryRun)
        {
            Lifecycle.ShutdownCoordinator.DryRun = true;
        }

        if (options.EnableTaskbarPill)
        {
            _taskbarPillView = _serviceProvider.GetRequiredService<ITaskbarPillView>();
            _ = _serviceProvider.GetRequiredService<TaskbarPillSubscriber>();
            _trayIconView.OnTaskbarCreated += _taskbarPillView.HandleTaskbarCreated;
        }
    }

    public void Dispose()
    {
        if (_taskbarPillView != null)
        {
            _trayIconView.OnTaskbarCreated -= _taskbarPillView.HandleTaskbarCreated;
        }

        _serviceProvider.Dispose();
        _rawPort.Dispose();
    }
}
