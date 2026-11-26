namespace VoltTray.Hosting;

/// <summary>
/// Immutable startup configuration parsed from command-line arguments.
/// </summary>
public sealed record ApplicationOptions(
    ApplicationRunMode RunMode,
    bool IsSimulation,
    bool IsDryRun,
    int PollingIntervalMs,
    bool IncludeAllHidDevices,
    string? RawInspectCommand,
    string? ExportPath,
    bool HasInspectCommandError,
    string? ErrorMessage);
