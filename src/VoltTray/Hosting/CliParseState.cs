using VoltTray.Core.Diagnostics;
using VoltTray.Shared;

namespace VoltTray.Hosting;

internal sealed class CliParseState
{
    public bool IsInspectMode { get; set; }
    public bool IsSimulation { get; set; }
    public bool IsDryRun { get; set; }
    public int PollingIntervalMs { get; set; } = ApplicationDefaults.DefaultPollingIntervalMs;
    public bool IncludeAllHidDevices { get; set; }
    public UpsDiagnosticInquiry Inquiry { get; set; } = UpsDiagnosticInquiry.QuickStatus;
    public string? ExportPath { get; set; }
    public string? FirstTrayOnlyFlag { get; private set; }
    public string? FirstInspectOnlyFlag { get; private set; }

    public void RecordScopeFlag(CliOptionScope scope, string flag)
    {
        if (scope == CliOptionScope.TrayOnly)
        {
            FirstTrayOnlyFlag ??= flag;
        }
        else if (scope == CliOptionScope.InspectOnly)
        {
            FirstInspectOnlyFlag ??= flag;
        }
    }
}
