using VoltTray.Core.Diagnostics;
using VoltTray.Infrastructure.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using VoltTray.Cli;
using VoltTray.Hosting;
using VoltTray.Infrastructure.Hid;
using VoltTray.Infrastructure.Protocol;
using VoltTray.Shared;
using VoltTray.Tests.ObjectMothers;
using Xunit;

namespace VoltTray.Tests;

public class ApplicationStartupTests
{
    [Fact]
    public void ApplicationOptionsParser_ParsesTrayDefaultsAndClampsSmallPositiveInterval()
    {
        ApplicationLaunchOptions defaults = ApplicationOptionsParser.Parse([]);
        TrayMonitorLaunchOptions trayDefaults = Assert.IsType<TrayMonitorLaunchOptions>(defaults);
        Assert.False(trayDefaults.IsSimulation);
        Assert.False(trayDefaults.IsDryRun);
        Assert.Equal(ApplicationDefaults.DefaultPollingIntervalMs, trayDefaults.PollingIntervalMs);

        ApplicationLaunchOptions custom = ApplicationOptionsParser.Parse(["--sim", "--dry-run", "--interval", "100"]);
        TrayMonitorLaunchOptions trayCustom = Assert.IsType<TrayMonitorLaunchOptions>(custom);
        Assert.True(trayCustom.IsSimulation);
        Assert.True(trayCustom.IsDryRun);
        Assert.Equal(ApplicationDefaults.MinimumPollingIntervalMs, trayCustom.PollingIntervalMs);
    }

    [Fact]
    public void ApplicationOptionsParser_ParsesValidInspectOptions()
    {
        ApplicationLaunchOptions parsed = ApplicationOptionsParser.Parse(
            ["--inspect", "--all-hid", "--cmd", "QGS", "--export", "capture.json"]);

        InspectLaunchOptions inspectOptions = Assert.IsType<InspectLaunchOptions>(parsed);
        Assert.Equal(UpsDiagnosticInquiry.GeneralStatus, inspectOptions.Inquiry);
        Assert.True(inspectOptions.IncludeAllHidDevices);
        Assert.Equal("capture.json", inspectOptions.ExportPath);
    }

    [Theory]
    [InlineData("--inspect", "--export", "--cmd", "QS")]
    [InlineData("--inspect", "--cmd", "--all-hid")]
    [InlineData("--interval", "--dry-run")]
    [InlineData("--inspect", "--cmd")]
    [InlineData("--inspect", "--export")]
    [InlineData("--interval")]
    [InlineData("--interval", "abc")]
    [InlineData("--interval", "0")]
    [InlineData("--interval", "-10")]
    [InlineData("--stream")]
    [InlineData("--unknown")]
    [InlineData("positional")]
    [InlineData("--cmd", "QS")]
    [InlineData("--export", "capture.json")]
    [InlineData("--all-hid")]
    [InlineData("--inspect", "--simulate")]
    [InlineData("--inspect", "--dry-run")]
    [InlineData("--inspect", "--interval", "500")]
    [InlineData("--inspect", "--cmd", "Q")]
    public void ApplicationOptionsParser_RejectsInvalidOrCrossModeArguments(params string[] args)
    {
        ApplicationLaunchOptions parsed = ApplicationOptionsParser.Parse(args);
        InvalidLaunchOptions invalid = Assert.IsType<InvalidLaunchOptions>(parsed);
        Assert.False(string.IsNullOrWhiteSpace(invalid.ErrorMessage));
    }

    [Fact]
    public void ApplicationModeStrategyFactory_SelectsExpectedStrategyByLaunchOptions()
    {
        using var output = new StringWriter();
        var probe = new FakeUpsDiagnosticProbe();
        var factory = new ApplicationModeStrategyFactory(probe, output);

        IApplicationModeStrategy trayStrategy = factory.Create(
            new TrayMonitorLaunchOptions(IsSimulation: true, IsDryRun: false, PollingIntervalMs: 1500));
        IApplicationModeStrategy inspectStrategy = factory.Create(
            new InspectLaunchOptions(UpsDiagnosticInquiry.QuickStatus, IncludeAllHidDevices: false, ExportPath: null));
        IApplicationModeStrategy invalidStrategy = factory.Create(
            new InvalidLaunchOptions("Bad argument."));

        Assert.IsType<TrayMonitorModeStrategy>(trayStrategy);
        Assert.IsType<InspectModeStrategy>(inspectStrategy);
        Assert.IsType<InvalidLaunchModeStrategy>(invalidStrategy);
    }

    [Fact]
    public void TrayMonitorModeStrategy_CreateUpsDevicePort_SelectsSimulatedOrWin32Port()
    {
        using var simulatedPort = TrayMonitorModeStrategy.CreateUpsDevicePort(
            new TrayMonitorLaunchOptions(IsSimulation: true, IsDryRun: false, PollingIntervalMs: 1500));
        using var hardwarePort = TrayMonitorModeStrategy.CreateUpsDevicePort(
            new TrayMonitorLaunchOptions(IsSimulation: false, IsDryRun: false, PollingIntervalMs: 1500));

        Assert.IsType<SimulatedUpsDevicePort>(simulatedPort);
        Assert.IsType<Win32HidUpsPort>(hardwarePort);
    }

    [Fact]
    public void InvalidLaunchModeStrategy_WritesErrorMessageAndReturnsExitCodeOne()
    {
        using var output = new StringWriter();
        var strategy = new InvalidLaunchModeStrategy(new InvalidLaunchOptions("Missing value for --cmd."), output);

        int exitCode = strategy.Execute();

        Assert.Equal(1, exitCode);
        Assert.Contains("Missing value for --cmd.", output.ToString());
    }

    [Fact]
    public void InspectModeStrategy_ScansSetupDiStrictlyOnceAndExportsJsonWithInvariantCulture()
    {
        HidDeviceDescriptor descriptor = new(
            @"\\?\HID#VID_0665&PID_5161#test",
            HidConstants.VendorId,
            HidConstants.ProductId,
            0x0100,
            "Forza",
            "NT-2401PRO",
            "",
            HidConstants.ReportLength,
            HidConstants.ReportLength);

        byte[] liveFrame = VoltronicFrameMother.Nt2401ProLiveFrame;
        UpsProbeCapture capture = ReadOnlyUpsProbe.BuildCapture(
            descriptor,
            UpsDiagnosticInquiry.QuickStatus,
            [[HidConstants.DefaultReportId, .. liveFrame]],
            liveFrame);

        var probe = new FakeUpsDiagnosticProbe([descriptor], capture);
        string exportPath = Path.Combine(Path.GetTempPath(), $"volttray-test-{Guid.NewGuid():N}.json");
        CultureInfo previousCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("es-ES");
            using var output = new StringWriter();
            var options = new InspectLaunchOptions(UpsDiagnosticInquiry.QuickStatus, IncludeAllHidDevices: true, exportPath);
            var strategy = new InspectModeStrategy(options, probe, output);

            int exitCode = strategy.Execute();

            Assert.Equal(0, exitCode);
            Assert.Equal(1, probe.DiscoverDevicesCallCount);
            Assert.Equal(1, probe.QueryDeviceCallCount);

            string cliText = output.ToString();
            Assert.DoesNotContain("--stream", cliText);
            Assert.Contains("Frequency: 59.97 Hz", cliText);
            Assert.Contains("Status2: 60 Hz, 24 V (2x12V), 120 V", cliText);

            string exportedJson = File.ReadAllText(exportPath);
            Assert.Contains("59.97", exportedJson);
            using var document = JsonDocument.Parse(exportedJson);
            Assert.Equal(59.97, document.RootElement.GetProperty("FrequencyHz").GetDouble());
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            if (File.Exists(exportPath))
            {
                File.Delete(exportPath);
            }
        }
    }

    [Fact]
    public void InspectModeStrategy_WhenNoDeviceOrNullCapture_ReturnsOne()
    {
        var emptyProbe = new FakeUpsDiagnosticProbe();
        using var emptyOutput = new StringWriter();
        var emptyStrategy = new InspectModeStrategy(
            new InspectLaunchOptions(UpsDiagnosticInquiry.QuickStatus, IncludeAllHidDevices: false, ExportPath: null),
            emptyProbe,
            emptyOutput);

        Assert.Equal(1, emptyStrategy.Execute());
        Assert.Equal(1, emptyProbe.DiscoverDevicesCallCount);
        Assert.Equal(0, emptyProbe.QueryDeviceCallCount);

        HidDeviceDescriptor descriptor = new(
            @"\\?\HID#VID_0665&PID_5161#test",
            HidConstants.VendorId,
            HidConstants.ProductId,
            0x0100,
            "Forza",
            "NT-2401PRO",
            "",
            HidConstants.ReportLength,
            HidConstants.ReportLength);

        var nullCaptureProbe = new FakeUpsDiagnosticProbe([descriptor], captureResult: null);
        using var nullOutput = new StringWriter();
        var nullCaptureStrategy = new InspectModeStrategy(
            new InspectLaunchOptions(UpsDiagnosticInquiry.QuickStatus, IncludeAllHidDevices: false, ExportPath: null),
            nullCaptureProbe,
            nullOutput);

        Assert.Equal(1, nullCaptureStrategy.Execute());
        Assert.Equal(1, nullCaptureProbe.DiscoverDevicesCallCount);
        Assert.Equal(1, nullCaptureProbe.QueryDeviceCallCount);
    }

    private sealed class FakeUpsDiagnosticProbe : IUpsDiagnosticProbe
    {
        private readonly IReadOnlyList<HidDeviceDescriptor> _descriptors;
        private readonly UpsProbeCapture? _captureResult;

        public FakeUpsDiagnosticProbe(
            IReadOnlyList<HidDeviceDescriptor>? descriptors = null,
            UpsProbeCapture? captureResult = null)
        {
            _descriptors = descriptors ?? Array.Empty<HidDeviceDescriptor>();
            _captureResult = captureResult;
        }

        public int DiscoverDevicesCallCount { get; private set; }
        public int QueryDeviceCallCount { get; private set; }

        public IReadOnlyList<HidDeviceDescriptor> DiscoverDevices(bool includeAllHidDevices = false)
        {
            DiscoverDevicesCallCount++;
            return _descriptors;
        }

        public UpsProbeCapture? QueryDevice(HidDeviceDescriptor device, UpsDiagnosticInquiry inquiry)
        {
            QueryDeviceCallCount++;
            return _captureResult;
        }
    }
}
