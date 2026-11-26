using VoltTray.Core.Telemetry;
using VoltTray.Core.Safety;
using VoltTray.Core.Diagnostics;
using VoltTray.Infrastructure.Diagnostics;
using VoltTray.Infrastructure.OS;
using VoltTray.UI.Dialogs;
using VoltTray.UI.Rendering;

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

    public HostLifecycle Lifecycle { get; }

    public TrayMonitorBootstrapper(TrayMonitorLaunchOptions options, Func<IUpsDevicePort> upsPortFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(upsPortFactory);

        var telemetryProbe = new InProcessTelemetryProbe();
        IExecutionBoundary executionBoundary = new SafeExecutionMiddleware(telemetryProbe);
        _rawPort = upsPortFactory();
        _decoratedPort = new DiagnosticUpsPortDecorator(_rawPort, telemetryProbe);
        _trayRenderer = new TrayRenderer();
        _popoverPainter = new PopoverPainter();

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
    }

    public void Dispose()
    {
        Lifecycle.Dispose();
        _popoverPainter.Dispose();
        _trayRenderer.Dispose();
        _decoratedPort.Dispose();
        _rawPort.Dispose();
    }
}
