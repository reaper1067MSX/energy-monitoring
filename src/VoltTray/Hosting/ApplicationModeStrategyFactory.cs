using VoltTray.Cli;
using VoltTray.Infrastructure.Hid;

namespace VoltTray.Hosting;

/// <summary>
/// Resolves the startup <see cref="IApplicationModeStrategy"/> from discriminated launch options.
/// </summary>
public sealed class ApplicationModeStrategyFactory
{
    private readonly IUpsDiagnosticProbe _diagnosticProbe;
    private readonly TextWriter _diagnosticOutput;

    public ApplicationModeStrategyFactory(
        IUpsDiagnosticProbe? diagnosticProbe = null,
        TextWriter? diagnosticOutput = null)
    {
        _diagnosticProbe = diagnosticProbe ?? new ReadOnlyUpsProbe();
        _diagnosticOutput = diagnosticOutput ?? Console.Out;
    }

    public IApplicationModeStrategy Create(ApplicationLaunchOptions options)
    {
        return options switch
        {
            TrayMonitorLaunchOptions trayOptions => new TrayMonitorModeStrategy(trayOptions),
            InspectLaunchOptions inspectOptions => new InspectModeStrategy(
                inspectOptions,
                _diagnosticProbe,
                _diagnosticOutput),
            InvalidLaunchOptions invalidOptions => new InvalidLaunchModeStrategy(
                invalidOptions,
                _diagnosticOutput),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options, "Unsupported launch options.")
        };
    }
}
