using System.Globalization;
using VoltTray.Core.Models;
using VoltTray.Infrastructure.Protocol;
using VoltTray.Shared;

namespace VoltTray.Hosting;

/// <summary>
/// Strictly parses command-line arguments via a data-driven flag rule registry
/// into discriminated <see cref="ApplicationLaunchOptions"/>.
/// </summary>
public static class ApplicationOptionsParser
{
    private static readonly IReadOnlyDictionary<string, CliFlagRule> FlagRules = BuildFlagRuleRegistry();

    public static ApplicationLaunchOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var state = new CliParseState();

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            if (!FlagRules.TryGetValue(argument, out CliFlagRule? rule))
            {
                return new InvalidLaunchOptions($"Unknown argument '{argument}'.");
            }

            state.RecordScopeFlag(rule.Scope, argument);

            string? rawValue = null;
            if (rule.RequiresValue)
            {
                if (!TryConsumeFlagValue(args, ref index, rule.Aliases[0], out string consumedValue, out string? missingError))
                {
                    return new InvalidLaunchOptions(missingError!);
                }

                rawValue = consumedValue;
            }

            string? applyError = rule.Apply(state, rawValue);
            if (applyError != null)
            {
                return new InvalidLaunchOptions(applyError);
            }
        }

        if (state.IsInspectMode && state.FirstTrayOnlyFlag != null)
        {
            return new InvalidLaunchOptions($"Flag '{state.FirstTrayOnlyFlag}' cannot be combined with {CliConstants.InspectFlag}.");
        }

        if (!state.IsInspectMode && state.FirstInspectOnlyFlag != null)
        {
            return new InvalidLaunchOptions($"Flag '{state.FirstInspectOnlyFlag}' is only valid with {CliConstants.InspectFlag}.");
        }

        return state.IsInspectMode
            ? new InspectLaunchOptions(state.Inquiry, state.IncludeAllHidDevices, state.ExportPath)
            : new TrayMonitorLaunchOptions(state.IsSimulation, state.IsDryRun, state.PollingIntervalMs);
    }

    private static IReadOnlyDictionary<string, CliFlagRule> BuildFlagRuleRegistry()
    {
        CliFlagRule[] rules =
        [
            new(
                Aliases: [CliConstants.InspectFlag],
                Scope: CliOptionScope.ModeSelector,
                RequiresValue: false,
                Apply: (state, _) =>
                {
                    state.IsInspectMode = true;
                    return null;
                }),
            new(
                Aliases: [CliConstants.SimulateFlag, CliConstants.SimFlag, CliConstants.MockFlag],
                Scope: CliOptionScope.TrayOnly,
                RequiresValue: false,
                Apply: (state, _) =>
                {
                    state.IsSimulation = true;
                    return null;
                }),
            new(
                Aliases: [CliConstants.DryRunFlag],
                Scope: CliOptionScope.TrayOnly,
                RequiresValue: false,
                Apply: (state, _) =>
                {
                    state.IsDryRun = true;
                    return null;
                }),
            new(
                Aliases: [CliConstants.IntervalFlag],
                Scope: CliOptionScope.TrayOnly,
                RequiresValue: true,
                Apply: ApplyInterval),
            new(
                Aliases: [CliConstants.AllHidFlag],
                Scope: CliOptionScope.InspectOnly,
                RequiresValue: false,
                Apply: (state, _) =>
                {
                    state.IncludeAllHidDevices = true;
                    return null;
                }),
            new(
                Aliases: [CliConstants.CommandFlag],
                Scope: CliOptionScope.InspectOnly,
                RequiresValue: true,
                Apply: ApplyCommand),
            new(
                Aliases: [CliConstants.ExportFlag],
                Scope: CliOptionScope.InspectOnly,
                RequiresValue: true,
                Apply: (state, value) =>
                {
                    state.ExportPath = value;
                    return null;
                })
        ];

        var registry = new Dictionary<string, CliFlagRule>(StringComparer.OrdinalIgnoreCase);
        foreach (CliFlagRule rule in rules)
        {
            foreach (string alias in rule.Aliases)
            {
                registry[alias] = rule;
            }
        }

        return registry;
    }

    private static string? ApplyInterval(CliParseState state, string? rawInterval)
    {
        bool isValidPositiveInteger =
            int.TryParse(rawInterval, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedInterval) &&
            parsedInterval > 0;

        if (!isValidPositiveInteger)
        {
            return $"Invalid value for {CliConstants.IntervalFlag}: '{rawInterval}'. Expected a positive integer.";
        }

        state.PollingIntervalMs = Math.Max(ApplicationDefaults.MinimumPollingIntervalMs, parsedInterval);
        return null;
    }

    private static string? ApplyCommand(CliParseState state, string? rawCommand)
    {
        string commandText = rawCommand ?? string.Empty;
        if (!VoltronicInquiryCodec.TryParseWireCommand(commandText, out UpsDiagnosticInquiry parsedInquiry))
        {
            string formattedCommand = commandText.Replace("\r", "\\r");
            return $"Command '{formattedCommand}' is blocked by the read-only diagnostic inquiry policy.";
        }

        state.Inquiry = parsedInquiry;
        return null;
    }

    private static bool TryConsumeFlagValue(
        string[] args,
        ref int index,
        string flagName,
        out string value,
        out string? errorMessage)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith(CliConstants.FlagPrefix, StringComparison.Ordinal))
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
