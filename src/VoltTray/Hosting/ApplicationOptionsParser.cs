using System.Globalization;
using VoltTray.Core.Models;
using VoltTray.Infrastructure.Protocol;
using VoltTray.Shared;

namespace VoltTray.Hosting;

/// <summary>
/// Strictly parses command-line arguments in a single sequential pass into discriminated <see cref="ApplicationLaunchOptions"/>.
/// </summary>
public static class ApplicationOptionsParser
{
    private const string FlagPrefix = "--";
    private const string InspectFlag = "--inspect";
    private const string SimulateFlag = "--simulate";
    private const string SimFlag = "--sim";
    private const string MockFlag = "--mock";
    private const string DryRunFlag = "--dry-run";
    private const string IntervalFlag = "--interval";
    private const string AllHidFlag = "--all-hid";
    private const string CommandFlag = "--cmd";
    private const string ExportFlag = "--export";

    public static ApplicationLaunchOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        bool isInspectMode = false;
        bool isSimulation = false;
        bool isDryRun = false;
        int pollingIntervalMs = ApplicationDefaults.DefaultPollingIntervalMs;
        bool includeAllHidDevices = false;
        UpsDiagnosticInquiry inquiry = UpsDiagnosticInquiry.QuickStatus;
        string? exportPath = null;
        string? firstTrayOnlyFlag = null;
        string? firstInspectOnlyFlag = null;

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];

            if (argument.Equals(InspectFlag, StringComparison.OrdinalIgnoreCase))
            {
                isInspectMode = true;
                continue;
            }

            if (argument.Equals(SimulateFlag, StringComparison.OrdinalIgnoreCase) ||
                argument.Equals(SimFlag, StringComparison.OrdinalIgnoreCase) ||
                argument.Equals(MockFlag, StringComparison.OrdinalIgnoreCase))
            {
                isSimulation = true;
                firstTrayOnlyFlag ??= argument;
                continue;
            }

            if (argument.Equals(DryRunFlag, StringComparison.OrdinalIgnoreCase))
            {
                isDryRun = true;
                firstTrayOnlyFlag ??= argument;
                continue;
            }

            if (argument.Equals(IntervalFlag, StringComparison.OrdinalIgnoreCase))
            {
                firstTrayOnlyFlag ??= argument;
                if (!TryConsumeFlagValue(args, ref index, IntervalFlag, out string rawInterval, out string? missingError))
                {
                    return new InvalidLaunchOptions(missingError!);
                }

                bool isValidPositiveInteger =
                    int.TryParse(rawInterval, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedInterval) &&
                    parsedInterval > 0;

                if (!isValidPositiveInteger)
                {
                    return new InvalidLaunchOptions(
                        $"Invalid value for {IntervalFlag}: '{rawInterval}'. Expected a positive integer.");
                }

                pollingIntervalMs = Math.Max(ApplicationDefaults.MinimumPollingIntervalMs, parsedInterval);
                continue;
            }

            if (argument.Equals(AllHidFlag, StringComparison.OrdinalIgnoreCase))
            {
                includeAllHidDevices = true;
                firstInspectOnlyFlag ??= argument;
                continue;
            }

            if (argument.Equals(CommandFlag, StringComparison.OrdinalIgnoreCase))
            {
                firstInspectOnlyFlag ??= argument;
                if (!TryConsumeFlagValue(args, ref index, CommandFlag, out string rawCommand, out string? missingError))
                {
                    return new InvalidLaunchOptions(missingError!);
                }

                if (!VoltronicInquiryCodec.TryParseWireCommand(rawCommand, out UpsDiagnosticInquiry parsedInquiry))
                {
                    string formattedCommand = rawCommand.Replace("\r", "\\r");
                    return new InvalidLaunchOptions(
                        $"Command '{formattedCommand}' is blocked by the read-only diagnostic inquiry policy.");
                }

                inquiry = parsedInquiry;
                continue;
            }

            if (argument.Equals(ExportFlag, StringComparison.OrdinalIgnoreCase))
            {
                firstInspectOnlyFlag ??= argument;
                if (!TryConsumeFlagValue(args, ref index, ExportFlag, out string rawExportPath, out string? missingError))
                {
                    return new InvalidLaunchOptions(missingError!);
                }

                exportPath = rawExportPath;
                continue;
            }

            return new InvalidLaunchOptions($"Unknown argument '{argument}'.");
        }

        if (isInspectMode && firstTrayOnlyFlag != null)
        {
            return new InvalidLaunchOptions($"Flag '{firstTrayOnlyFlag}' cannot be combined with {InspectFlag}.");
        }

        if (!isInspectMode && firstInspectOnlyFlag != null)
        {
            return new InvalidLaunchOptions($"Flag '{firstInspectOnlyFlag}' is only valid with {InspectFlag}.");
        }

        return isInspectMode
            ? new InspectLaunchOptions(inquiry, includeAllHidDevices, exportPath)
            : new TrayMonitorLaunchOptions(isSimulation, isDryRun, pollingIntervalMs);
    }

    private static bool TryConsumeFlagValue(
        string[] args,
        ref int index,
        string flagName,
        out string value,
        out string? errorMessage)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith(FlagPrefix, StringComparison.Ordinal))
        {
            value = string.Empty;
            errorMessage = $"Missing value for {flagName}.";
            return false;
        }

        value = args[++index];
        errorMessage = null;
        return true;
    }
}
