using Microsoft.Extensions.DependencyInjection;
using VoltTray.Core.Diagnostics;
using VoltTray.Core.Safety;
using VoltTray.Core.Telemetry;
using VoltTray.Infrastructure.Diagnostics;
using VoltTray.Infrastructure.OS;
using VoltTray.Subscribers;
using VoltTray.UI.Atoms;
using VoltTray.UI.Dialogs;
using VoltTray.UI.Popover;
using VoltTray.UI.TaskbarPill;
using VoltTray.UI.Tray;

namespace VoltTray.Hosting;

/// <summary>
/// Composition root for the interactive tray monitor using Microsoft.Extensions.DependencyInjection.
/// Builds the singleton object graph once and disposes the ServiceProvider on shutdown.
/// </summary>
public sealed class TrayMonitorBootstrapper : IDisposable
{
    private readonly IUpsDevicePort _rawPort;
    private readonly ServiceProvider _serviceProvider;
    private readonly TrayRenderer _trayRenderer;
    private readonly TaskbarPillWindow? _taskbarPill;

    public HostLifecycle Lifecycle { get; }

    public TrayMonitorBootstrapper(TrayMonitorLaunchOptions options, Func<IUpsDevicePort> upsPortFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(upsPortFactory);

        _rawPort = upsPortFactory();

        var services = new ServiceCollection();
        services.AddSingleton<InProcessTelemetryProbe>();
        services.AddSingleton<IExecutionBoundary>(sp => new SafeExecutionMiddleware(sp.GetRequiredService<InProcessTelemetryProbe>()));
        services.AddSingleton<IUpsDevicePort>(sp => new DiagnosticUpsPortDecorator(_rawPort, sp.GetRequiredService<InProcessTelemetryProbe>()));
        services.AddSingleton<ISafetyShutdownPort, WindowsShutdownPort>();
        services.AddSingleton<INotificationPort, WindowsNotificationPort>();
        services.AddSingleton<IScreenPlacementProvider, Win32ScreenPlacementProvider>();

        services.AddSingleton<ISvgIconLibrary, SkiaSvgIconLibrary>();
        services.AddSingleton<ITrayIconRasterizer>(_ => TrayIconRasterizer.CreateDefault());
        services.AddSingleton<INativeIconFactory, Win32NativeIconFactory>();
        services.AddSingleton<IPopoverSurfaceRenderer>(sp => new SkiaPopoverSurfaceRenderer(sp.GetRequiredService<ISvgIconLibrary>()));

        services.AddSingleton(sp => new TrayRenderer(
            sp.GetRequiredService<ITrayIconRasterizer>(),
            sp.GetRequiredService<INativeIconFactory>(),
            WindowsThemeDetector.IsLightTaskbar));
        services.AddSingleton<ITrayIconView>(sp => sp.GetRequiredService<TrayRenderer>());

        services.AddSingleton(sp => new PopoverPainter(
            enableNativeWindow: true,
            surfaceRenderer: sp.GetRequiredService<IPopoverSurfaceRenderer>()));
        services.AddSingleton<IPopoverView>(sp => sp.GetRequiredService<PopoverPainter>());

        services.AddSingleton<IContextMenuView>(_ => new Win32ContextMenu(enableNativeMenu: true));
        services.AddSingleton<IExitDialogView>(_ => new ExitDialog(enableNativeDialog: true));

        services.AddSingleton(sp => new HostLifecycle(
            sp.GetRequiredService<IUpsDevicePort>(),
            sp.GetRequiredService<ISafetyShutdownPort>(),
            sp.GetRequiredService<INotificationPort>(),
            sp.GetRequiredService<ITrayIconView>(),
            sp.GetRequiredService<IPopoverView>(),
            sp.GetRequiredService<IExitDialogView>(),
            sp.GetRequiredService<IScreenPlacementProvider>(),
            sp.GetRequiredService<InProcessTelemetryProbe>(),
            sp.GetRequiredService<IExecutionBoundary>(),
            delegateDiagnosticsToPort: true,
            eventBus: null,
            appThemeProvider: WindowsThemeDetector.GetAppTheme,
            contextMenu: sp.GetRequiredService<IContextMenuView>()));

        if (options.EnableTaskbarPill)
        {
            services.AddSingleton<ITaskbarPillPlacementCalculator, TaskbarPillPlacementCalculator>();
            services.AddSingleton<ITaskbarPillSurfaceRenderer>(sp => new TaskbarPillSurfaceRenderer(
                sp.GetRequiredService<ITrayIconRasterizer>(),
                sp.GetRequiredService<ISvgIconLibrary>()));
            services.AddSingleton<IFullscreenStateProvider, Win32FullscreenStateProvider>();
            services.AddSingleton(sp => new TaskbarPillWindow(
                sp.GetRequiredService<ITaskbarPillPlacementCalculator>(),
                sp.GetRequiredService<ITaskbarPillSurfaceRenderer>(),
                sp.GetRequiredService<IFullscreenStateProvider>(),
                WindowsThemeDetector.IsLightTaskbar,
                enableNativeWindow: true));
            services.AddSingleton<ITaskbarPillView>(sp => sp.GetRequiredService<TaskbarPillWindow>());
            services.AddSingleton(sp => new TaskbarPillSubscriber(
                sp.GetRequiredService<HostLifecycle>().EventBus,
                sp.GetRequiredService<ITaskbarPillView>(),
                sp.GetRequiredService<HostLifecycle>()));
        }

        _serviceProvider = services.BuildServiceProvider();
        Lifecycle = _serviceProvider.GetRequiredService<HostLifecycle>();
        _trayRenderer = _serviceProvider.GetRequiredService<TrayRenderer>();

        if (options.IsDryRun)
        {
            Lifecycle.ShutdownCoordinator.DryRun = true;
        }

        if (options.EnableTaskbarPill)
        {
            _taskbarPill = _serviceProvider.GetRequiredService<TaskbarPillWindow>();
            _ = _serviceProvider.GetRequiredService<TaskbarPillSubscriber>();
            _trayRenderer.OnTaskbarCreated += _taskbarPill.HandleTaskbarCreated;
        }
    }

    public void Dispose()
    {
        if (_taskbarPill != null)
        {
            _trayRenderer.OnTaskbarCreated -= _taskbarPill.HandleTaskbarCreated;
        }

        _serviceProvider.Dispose();
        _rawPort.Dispose();
    }
}
