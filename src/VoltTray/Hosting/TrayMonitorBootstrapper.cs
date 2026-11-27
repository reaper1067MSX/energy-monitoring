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
    private readonly ITaskbarPillView _taskbarPillView;
    private readonly IUserSettingsStore _settingsStore;
    private TrayIconMode _iconMode;

    public IHostLifecycle Lifecycle { get; }

    public TrayMonitorBootstrapper(TrayMonitorLaunchOptions options, Func<IUpsDevicePort> upsPortFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(upsPortFactory);

        _rawPort = upsPortFactory();

        var services = new ServiceCollection();
        services.AddVoltTrayInfrastructure(() => _rawPort);
        using ServiceProvider probeProvider = services.BuildServiceProvider();
        IUserSettingsStore initialStore = probeProvider.GetRequiredService<IUserSettingsStore>();
        _iconMode = ResolveInitialIconMode(options, initialStore);

        services.AddVoltTrayUi(enableTaskbarPill: true, showTrayIcon: _iconMode == TrayIconMode.Short);

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
            contextMenu: sp.GetRequiredService<IContextMenuView>(),
            isCompactIconModeProvider: () => _iconMode == TrayIconMode.Short,
            toggleIconModeAction: ToggleIconMode));

        services.AddSingleton(sp => new TaskbarPillSubscriber(
            sp.GetRequiredService<IHostLifecycle>().EventBus,
            sp.GetRequiredService<ITaskbarPillView>(),
            sp.GetRequiredService<IHostLifecycle>(),
            () => _iconMode == TrayIconMode.Large));

        _serviceProvider = services.BuildServiceProvider();
        _settingsStore = _serviceProvider.GetRequiredService<IUserSettingsStore>();
        Lifecycle = _serviceProvider.GetRequiredService<IHostLifecycle>();
        _trayIconView = _serviceProvider.GetRequiredService<ITrayIconView>();
        _taskbarPillView = _serviceProvider.GetRequiredService<ITaskbarPillView>();
        _ = _serviceProvider.GetRequiredService<TaskbarPillSubscriber>();
        _trayIconView.OnTaskbarCreated += HandleTaskbarCreated;

        if (options.IsDryRun)
        {
            Lifecycle.ShutdownCoordinator.DryRun = true;
        }
    }

    public void Dispose()
    {
        _trayIconView.OnTaskbarCreated -= HandleTaskbarCreated;
        _serviceProvider.Dispose();
        _rawPort.Dispose();
    }

    private static TrayIconMode ResolveInitialIconMode(TrayMonitorLaunchOptions options, IUserSettingsStore settingsStore)
    {
        if (options.SelectedIconMode is TrayIconMode explicitMode)
        {
            settingsStore.SaveIconMode(explicitMode);
            return explicitMode;
        }

        if (settingsStore.LoadIconMode() is TrayIconMode savedMode)
        {
            return savedMode;
        }

        return options.EnableTaskbarPill ? TrayIconMode.Large : TrayIconMode.Short;
    }

    private void ToggleIconMode()
    {
        _iconMode = _iconMode == TrayIconMode.Large ? TrayIconMode.Short : TrayIconMode.Large;
        _settingsStore.SaveIconMode(_iconMode);

        bool isShort = _iconMode == TrayIconMode.Short;
        _trayIconView.SetTrayIconVisibility(isShort);

        if (isShort)
        {
            _taskbarPillView.Hide();
        }
        else if (Lifecycle.CurrentTelemetry is UpsTelemetry telemetry)
        {
            _taskbarPillView.Update(telemetry);
        }
    }

    private void HandleTaskbarCreated()
    {
        if (_iconMode == TrayIconMode.Large)
        {
            _taskbarPillView.HandleTaskbarCreated();
        }
    }
}
