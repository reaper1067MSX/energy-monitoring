using VoltTray.Infrastructure.Protocol;

namespace VoltTray.Hosting;

/// <summary>
/// Parses command-line arguments into an immutable <see cref="ApplicationOptions"/> instance.
/// </summary>
public static class ApplicationOptionsParser
{
    public const int DefaultPollingIntervalMs = 1500;
    public const int MinimumPollingIntervalMs = 250;

    private const string InspectFlag = "--inspect";
    private const string SimulateFlag = "--simulate";
    private const string SimFlag = "--sim";
    private const string MockFlag = "--mock";
    private const string DryRunFlag = "--dry-run";
    private const string IntervalFlag = "--interval";
    private const string AllHidFlag = "--all-hid";
    private const string CommandFlag = "--cmd";
    private const string ExportFlag = "--export";

    public static ApplicationOptions Parse(string[] args)
    {
        bool isInspectMode = HasFlag(args, InspectFlag);
        ApplicationRunMode runMode = isInspectMode
            ? ApplicationRunMode.DiagnosticInspect
            : ApplicationRunMode.TrayMonitor;

        bool isSimulation =
            HasFlag(args, SimulateFlag) ||
            HasFlag(args, SimFlag) ||
            HasFlag(args, MockFlag);

        bool isDryRun = HasFlag(args, DryRunFlag);
        bool includeAllHidDevices = HasFlag(args, AllHidFlag);
        int pollingIntervalMs = ParsePollingIntervalMs(args);

        string rawInspectCommand = VoltronicProtocolConstants.QueryStatusCommand;
        string? exportPath = null;
        bool hasInspectCommandError = false;
        string? errorMessage = null;

        for (int index = 0; index < args.Length; index++)
        {
            if (args[index].Equals(CommandFlag, StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    hasInspectCommandError = true;
                    errorMessage = "Missing value for --cmd.";
                    break;
                }

                rawInspectCommand = args[++index];
                continue;
            }

            if (args[index].Equals(ExportFlag, StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    hasInspectCommandError = true;
                    errorMessage = "Missing value for --export.";
                    break;
                }

                exportPath = args[++index];
            }
        }

        return new ApplicationOptions(
            runMode,
            isSimulation,
            isDryRun,
            pollingIntervalMs,
            includeAllHidDevices,
            rawInspectCommand,
            exportPath,
            hasInspectCommandError,
            errorMessage);
    }

    private static bool HasFlag(string[] args, string flag)
    {
        return args.Any(argument => argument.Equals(flag, StringComparison.OrdinalIgnoreCase));
    }

    private static int ParsePollingIntervalMs(string[] args)
    {
        int intervalMs = DefaultPollingIntervalMs;

        for (int index = 0; index < args.Length - 1; index++)
        {
            if (args[index].Equals(IntervalFlag, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[index + 1], out int parsedInterval))
            {
                intervalMs = Math.Max(MinimumPollingIntervalMs, parsedInterval);
            }
        }

        return intervalMs;
    }
}
