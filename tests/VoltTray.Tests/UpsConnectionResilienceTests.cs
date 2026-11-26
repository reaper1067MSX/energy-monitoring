using VoltTray.UI.Tray;
using VoltTray.UI.Popover;
using VoltTray.Core.Telemetry;
using VoltTray.Core.Diagnostics;
using System.Diagnostics;
using System.Text.Json;
using VoltTray.Infrastructure.Diagnostics;
using VoltTray.Infrastructure.Hid;
using VoltTray.Infrastructure.Protocol;
using VoltTray.Shared;
using VoltTray.Tests.ObjectMothers;
using VoltTray.UI.Models;

namespace VoltTray.Tests;

public class UpsConnectionResilienceTests
{
    private const int DisconnectCancellationBudgetMs = 100;

    [Fact]
    public async Task ConnectAsync_TransientSharingViolation_RetriesAndSucceedsOnSecondAttempt()
    {
        var openAttempts = 0;
        var connection = new ImmediateOkHidConnection();
        var transport = new ScriptedResultTransport(_ =>
        {
            openAttempts++;
            if (openAttempts == 1)
            {
                return Result<IHidConnection>.Failure(new DomainError(
                    SharedErrorConstants.PortBusyContentionCode,
                    $"Busy ({SharedErrorConstants.DiagnosticDisableViewPowerScript})",
                    ErrorCategory.PortBusyContention,
                    IsTransient: true));
            }

            return Result<IHidConnection>.Success(connection);
        });

        var retryPolicy = new AsyncRetryPolicy((_, _) => Task.CompletedTask);
        using var port = new Win32HidUpsPort(
            preferredDevicePath: @"\\?\HID#VID_0665&PID_5161#retry",
            allowAutoDiscovery: false,
            estimator: null,
            deviceScanner: null,
            transport: transport,
            negotiateOnConnect: false,
            retryPolicy: retryPolicy);

        Result<string> result = await port.ConnectAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(2, openAttempts);
        Assert.True(port.IsConnected);
    }

    [Fact]
    public async Task ConnectAsync_DeviceNotFound_FailsImmediatelyWithZeroBurstRetries()
    {
        var openAttempts = 0;
        var transport = new ScriptedResultTransport(_ =>
        {
            openAttempts++;
            return Result<IHidConnection>.Failure(new DomainError(
                SharedErrorConstants.DeviceNotFoundCode,
                "Device not found",
                ErrorCategory.DeviceNotFound,
                IsTransient: false));
        });

        var retryPolicy = new AsyncRetryPolicy((_, _) => Task.CompletedTask);
        using var port = new Win32HidUpsPort(
            preferredDevicePath: @"\\?\HID#VID_0665&PID_5161#missing",
            allowAutoDiscovery: false,
            estimator: null,
            deviceScanner: null,
            transport: transport,
            negotiateOnConnect: false,
            retryPolicy: retryPolicy);

        Result<string> result = await port.ConnectAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.DeviceNotFound, result.Error!.Category);
        Assert.Equal(1, openAttempts);
    }

    [Fact]
    public async Task ConnectAsync_PersistentPortBusyContention_ReportsDisableViewPowerGuidance()
    {
        var openAttempts = 0;
        var transport = new ScriptedResultTransport(_ =>
        {
            openAttempts++;
            return Result<IHidConnection>.Failure(new DomainError(
                SharedErrorConstants.PortBusyContentionCode,
                $"USB HID port is busy or locked ({SharedErrorConstants.DiagnosticDisableViewPowerScript}).",
                ErrorCategory.PortBusyContention,
                IsTransient: true));
        });

        var retryPolicy = new AsyncRetryPolicy((_, _) => Task.CompletedTask);
        using var port = new Win32HidUpsPort(
            preferredDevicePath: @"\\?\HID#VID_0665&PID_5161#locked",
            allowAutoDiscovery: false,
            estimator: null,
            deviceScanner: null,
            transport: transport,
            negotiateOnConnect: false,
            retryPolicy: retryPolicy);

        Result<string> result = await port.ConnectAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.PortBusyContention, result.Error!.Category);
        Assert.Contains(SharedErrorConstants.DiagnosticDisableViewPowerScript, result.Error.Message);
        Assert.Equal(HidConstants.MaxConnectAttempts, openAttempts);
    }

    [Fact]
    public async Task ConnectAsync_DisconnectDuringRetryDelay_CancelsUnder100Ms()
    {
        var delayEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new ScriptedResultTransport(_ => Result<IHidConnection>.Failure(new DomainError(
            SharedErrorConstants.PortBusyContentionCode,
            "Transient lock",
            ErrorCategory.PortBusyContention,
            IsTransient: true)));

        var retryPolicy = new AsyncRetryPolicy(async (_, token) =>
        {
            delayEntered.TrySetResult(true);
            await Task.Delay(TimeSpan.FromSeconds(5), token);
        });

        using var port = new Win32HidUpsPort(
            preferredDevicePath: @"\\?\HID#VID_0665&PID_5161#cancel",
            allowAutoDiscovery: false,
            estimator: null,
            deviceScanner: null,
            transport: transport,
            negotiateOnConnect: false,
            retryPolicy: retryPolicy);

        Task<Result<string>> connectTask = port.ConnectAsync();
        await delayEntered.Task;

        var stopwatch = Stopwatch.StartNew();
        port.Disconnect();
        Result<string> result = await connectTask;
        stopwatch.Stop();

        Assert.True(result.IsFailure);
        Assert.True(
            stopwatch.ElapsedMilliseconds < DisconnectCancellationBudgetMs,
            $"Disconnect cancellation took {stopwatch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task SafeExecutionMiddleware_And_HostLifecycle_ContainPortExceptionAndDumpDiagnostics()
    {
        string tempDiagnosticsDir = CreateTempDiagnosticsDirectory();
        try
        {
            var probe = new InProcessTelemetryProbe(tempDiagnosticsDir);
            IExecutionBoundary boundary = new SafeExecutionMiddleware(probe);
            using var throwingPort = new ThrowingUpsPort();
            using var tray = new NoOpTrayIconView();
            using var popover = new NoOpPopoverView();

            using var lifecycle = new HostLifecycle(
                throwingPort,
                null,
                null,
                tray,
                popover,
                null,
                null,
                probe,
                boundary);

            UpsTelemetry? result = await lifecycle.PollOnceAsync();

            Assert.Null(result);
            Assert.Equal(UpsOperationalState.CommunicationsLost, lifecycle.OperationalState);
            Assert.Equal(1, probe.Count);
            Assert.True(Directory.Exists(tempDiagnosticsDir));

            string[] dumpFiles = Directory.GetFiles(tempDiagnosticsDir, "anomaly-*.jsonl");
            Assert.NotEmpty(dumpFiles);
            string jsonLine = File.ReadAllLines(dumpFiles[^1])[^1];
            using JsonDocument doc = JsonDocument.Parse(jsonLine);
            Assert.Equal(nameof(TelemetryAnomalyKind.SystemException), doc.RootElement.GetProperty("Anomaly").GetString());
            Assert.Contains(nameof(InvalidOperationException), doc.RootElement.GetProperty("UiSummary").GetString());
        }
        finally
        {
            DeleteTempDirectory(tempDiagnosticsDir);
        }
    }

    [Fact]
    public async Task DiagnosticUpsPortDecorator_RecordsRealNullPreservingRawHex_AndQuarantinesImplausibleFrequency()
    {
        string tempDiagnosticsDir = CreateTempDiagnosticsDirectory();
        try
        {
            var probe = new InProcessTelemetryProbe(tempDiagnosticsDir);
            byte[] healthyRawFrame = VoltronicFrameMother.Nt2401ProLiveFrame;
            UpsTelemetry? healthyTelemetry = new VoltronicParser().Parse(healthyRawFrame);
            Assert.NotNull(healthyTelemetry);

            var innerPort = new RawSampleScriptedPort(
            [
                new RawUpsTelemetrySample(healthyRawFrame, healthyTelemetry),
                new RawUpsTelemetrySample(healthyRawFrame, healthyTelemetry with { FrequencyHz = 4.27 })
            ]);

            using IUpsDevicePort decorated = new DiagnosticUpsPortDecorator(innerPort, probe);

            UpsTelemetry? first = await decorated.ReadTelemetryAsync();
            Assert.Same(healthyTelemetry, first);
            Assert.False(Directory.Exists(tempDiagnosticsDir));

            UpsTelemetry? second = await decorated.ReadTelemetryAsync();
            Assert.Same(healthyTelemetry, second);
            Assert.Equal(2, probe.Count);

            string[] dumpFiles = Directory.GetFiles(tempDiagnosticsDir, "anomaly-*.jsonl");
            Assert.Single(dumpFiles);

            string[] lines = File.ReadAllLines(dumpFiles[0]);
            Assert.Equal(2, lines.Length);

            string expectedRawHex = Convert.ToHexString(healthyRawFrame);
            Assert.Contains("00", expectedRawHex);

            using JsonDocument healthyEntry = JsonDocument.Parse(lines[0]);
            Assert.Equal(expectedRawHex, healthyEntry.RootElement.GetProperty("RawHex").GetString());
        }
        finally
        {
            DeleteTempDirectory(tempDiagnosticsDir);
        }
    }

    private static string CreateTempDiagnosticsDirectory()
    {
        return Path.Combine(Path.GetTempPath(), $"volttray-resilience-{Guid.NewGuid():N}");
    }

    private static void DeleteTempDirectory(string directoryPath)
    {
        if (Directory.Exists(directoryPath))
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    private sealed class ScriptedResultTransport(Func<string, Result<IHidConnection>> opener) : IHidTransport
    {
        public Result<IHidConnection> Open(string devicePath) => opener(devicePath);
    }

    private sealed class ImmediateOkHidConnection : IHidConnection
    {
        public bool IsOpen { get; private set; } = true;
        public void CancelPendingIo() { }
        public void DrainInputBuffer(int drainTimeoutMs) { }
        public bool WriteCommand(string command) => IsOpen;
        public int ReadReport(byte[] buffer, int timeoutMs) => 0;
        public void Dispose() => IsOpen = false;
    }

    private sealed class ThrowingUpsPort : IUpsDevicePort
    {
        public bool IsConnected => true;
        public string? ActiveDevicePath => "MockFaulty";
        public bool TryConnect(string? customPath = null) => true;
        public Task<Result<string>> ConnectAsync(string? customPath = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<string>.Success("MockFaulty"));
        public void Disconnect() { }
        public Task<UpsTelemetry?> ReadTelemetryAsync(DateTimeOffset? now = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated hardware bridge fault");
        public Task<bool> ToggleBeeperAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> TriggerSelfTestAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public void Dispose() { }
    }

    private sealed class RawSampleScriptedPort(IReadOnlyList<RawUpsTelemetrySample> samples) : IUpsDevicePort, IRawTelemetryPort
    {
        private int _index;

        public bool IsConnected => true;
        public string? ActiveDevicePath => "RawScripted";
        public bool TryConnect(string? customPath = null) => true;
        public Task<Result<string>> ConnectAsync(string? customPath = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<string>.Success("RawScripted"));
        public void Disconnect() { }

        public Task<RawUpsTelemetrySample?> ReadRawTelemetryAsync(
            DateTimeOffset? now = null,
            CancellationToken cancellationToken = default)
        {
            RawUpsTelemetrySample? sample = _index < samples.Count ? samples[_index++] : null;
            return Task.FromResult(sample);
        }

        public async Task<UpsTelemetry?> ReadTelemetryAsync(
            DateTimeOffset? now = null,
            CancellationToken cancellationToken = default)
        {
            RawUpsTelemetrySample? sample = await ReadRawTelemetryAsync(now, cancellationToken);
            return sample?.Telemetry;
        }

        public Task<bool> ToggleBeeperAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> TriggerSelfTestAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public void Dispose() { }
    }

    private sealed class NoOpTrayIconView : ITrayIconView
    {
        public event Action? OnTrayIconClicked { add { } remove { } }
        public event Action? OnContextMenuRequested { add { } remove { } }
        public void UpdateIcon(UpsOperationalState state, int batteryPct, int dpi) { }
        public void SetTooltip(string text) { }
        public void Recreate() { }
        public ViewRect? TryGetIconRect() => null;
        public void Dispose() { }
    }

    private sealed class NoOpPopoverView : IPopoverView
    {
        public event Action? OnSelfTestRequested { add { } remove { } }
        public event Action? OnToggleBeeperRequested { add { } remove { } }
        public bool IsVisible => false;
        public UITheme CurrentTheme => UITheme.Dark;
        public void Show(UpsTelemetry telemetry, UITheme theme, ViewRect iconRect, TaskbarEdge edge, ViewRect workArea) { }
        public void Hide() { }
        public void Dispose() { }
    }
}
