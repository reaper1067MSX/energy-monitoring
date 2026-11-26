using VoltTray.Subscribers;
using VoltTray.UI.Atoms;
using VoltTray.UI.TaskbarPill;
using VoltTray.UI.Tray;
using VoltTray.UI.Popover;
using VoltTray.Core.Telemetry;
using VoltTray.Core.Safety;
using VoltTray.Core.Diagnostics;
using VoltTray.Infrastructure.Diagnostics;
using VoltTray.Infrastructure.OS;
using VoltTray.UI.Dialogs;

namespace VoltTray.Hosting;

/// <summary>
/// Composition root for the interactive tray monitor: builds the object graph
/// once and owns every disposable it creates.
/// </summary>
public sealed class TrayMonitorBootstrapper : IDisposable
{
    private readonly IUpsDevicePort _rawPort;
    private readonly IUpsDevicePort _decoratedPort;
    private readonly TrayRenderer _trayRenderer;
    private readonly PopoverPainter _popoverPainter;
    private readonly TaskbarPillWindow? _taskbarPill;
    private readonly TaskbarPillSubscriber? _taskbarPillSubscriber;

    public HostLifecycle Lifecycle { get; }

    public TrayMonitorBootstrapper(TrayMonitorLaunchOptions options, Func<IUpsDevicePort> upsPortFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(upsPortFactory);

        var telemetryProbe = new InProcessTelemetryProbe();
        IExecutionBoundary executionBoundary = new SafeExecutionMiddleware(telemetryProbe);
        _rawPort = upsPortFactory();
        _decoratedPort = new DiagnosticUpsPortDecorator(_rawPort, telemetryProbe);
        ITrayIconRasterizer trayIconRasterizer = TrayIconRasterizer.CreateDefault();
        INativeIconFactory nativeIconFactory = new Win32NativeIconFactory();
        _trayRenderer = new TrayRenderer(trayIconRasterizer, nativeIconFactory, WindowsThemeDetector.IsLightTaskbar);
        _popoverPainter = new PopoverPainter(enableNativeWindow: true);

        Lifecycle = new HostLifecycle(
            _decoratedPort,
            new WindowsShutdownPort(),
            new WindowsNotificationPort(),
            _trayRenderer,
            _popoverPainter,
            new ExitDialog(),
            screenPlacementProvider: null,
            telemetryProbe,
            executionBoundary,
            delegateDiagnosticsToPort: true);

        if (options.IsDryRun)
        {
            Lifecycle.ShutdownCoordinator.DryRun = true;
        }

        if (options.EnableTaskbarPill)
        {
            _taskbarPill = new TaskbarPillWindow(
                new TaskbarPillPlacementCalculator(),
                new TaskbarPillSurfaceRenderer(trayIconRasterizer),
                new Win32FullscreenStateProvider(),
                WindowsThemeDetector.IsLightTaskbar,
                enableNativeWindow: true);
            _taskbarPillSubscriber = new TaskbarPillSubscriber(Lifecycle.EventBus, _taskbarPill, Lifecycle);
        }
    }

    public void Dispose()
    {
        _taskbarPillSubscriber?.Dispose();
        _taskbarPill?.Dispose();
        Lifecycle.Dispose();
        _popoverPainter.Dispose();
        _trayRenderer.Dispose();
        _decoratedPort.Dispose();
        _rawPort.Dispose();
    }
}
