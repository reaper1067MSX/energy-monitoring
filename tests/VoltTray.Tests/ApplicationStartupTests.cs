using System.IO;
using VoltTray.Cli;
using VoltTray.Hosting;
using VoltTray.Infrastructure.Hid;
using VoltTray.Infrastructure.Protocol;
using Xunit;

namespace VoltTray.Tests;

public class ApplicationStartupTests
{
    [Fact]
    public void ApplicationOptionsParser_ParsesTrayDefaultsAndFlags()
    {
        ApplicationOptions defaults = ApplicationOptionsParser.Parse([]);
        Assert.Equal(ApplicationRunMode.TrayMonitor, defaults.RunMode);
        Assert.False(defaults.IsSimulation);
        Assert.False(defaults.IsDryRun);
        Assert.Equal(ApplicationOptionsParser.DefaultPollingIntervalMs, defaults.PollingIntervalMs);

        ApplicationOptions simDryRun = ApplicationOptionsParser.Parse(["--sim", "--dry-run", "--interval", "100"]);
        Assert.Equal(ApplicationRunMode.TrayMonitor, simDryRun.RunMode);
        Assert.True(simDryRun.IsSimulation);
        Assert.True(simDryRun.IsDryRun);
        Assert.Equal(ApplicationOptionsParser.MinimumPollingIntervalMs, simDryRun.PollingIntervalMs);
    }

    [Fact]
    public void ApplicationOptionsParser_ParsesInspectArgumentsAndMissingValueErrors()
    {
        ApplicationOptions inspect = ApplicationOptionsParser.Parse(
            ["--inspect", "--all-hid", "--cmd", "QGS", "--export", "capture.json"]);

        Assert.Equal(ApplicationRunMode.DiagnosticInspect, inspect.RunMode);
        Assert.True(inspect.IncludeAllHidDevices);
        Assert.Equal("QGS", inspect.RawInspectCommand);
        Assert.Equal("capture.json", inspect.ExportPath);
        Assert.False(inspect.HasInspectCommandError);

        ApplicationOptions missingCmd = ApplicationOptionsParser.Parse(["--inspect", "--cmd"]);
        Assert.True(missingCmd.HasInspectCommandError);
        Assert.Equal("Missing value for --cmd.", missingCmd.ErrorMessage);
    }

    [Fact]
    public void ApplicationModeStrategyFactory_SelectsExpectedStrategyByRunMode()
    {
        ApplicationOptions trayOptions = ApplicationOptionsParser.Parse([]);
        ApplicationOptions inspectOptions = ApplicationOptionsParser.Parse(["--inspect"]);

        Assert.IsType<TrayMonitorModeStrategy>(ApplicationModeStrategyFactory.Create(trayOptions));

        using var output = new StringWriter();
        Assert.IsType<InspectModeStrategy>(ApplicationModeStrategyFactory.Create(inspectOptions, output));
    }

    [Fact]
    public void UpsDevicePortFactory_SelectsSimulatedOrWin32Port()
    {
        ApplicationOptions simulatedOptions = ApplicationOptionsParser.Parse(["--simulate"]);
        ApplicationOptions hardwareOptions = ApplicationOptionsParser.Parse([]);

        using var simulatedPort = UpsDevicePortFactory.Create(simulatedOptions);
        using var hardwarePort = UpsDevicePortFactory.Create(hardwareOptions);

        Assert.IsType<SimulatedUpsDevicePort>(simulatedPort);
        Assert.IsType<Win32HidUpsPort>(hardwarePort);
    }
}
