using VoltTray.Cli;

namespace VoltTray.Hosting;

/// <summary>
/// Resolves the startup <see cref="IApplicationModeStrategy"/> from parsed options.
/// </summary>
public static class ApplicationModeStrategyFactory
{
    public static IApplicationModeStrategy Create(ApplicationOptions options, TextWriter? inspectOutput = null)
    {
        return options.RunMode switch
        {
            ApplicationRunMode.DiagnosticInspect => new InspectModeStrategy(inspectOutput ?? Console.Out),
            ApplicationRunMode.TrayMonitor => new TrayMonitorModeStrategy(),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.RunMode, "Unsupported run mode.")
        };
    }
}
