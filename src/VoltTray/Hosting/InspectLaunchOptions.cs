using VoltTray.Core.Diagnostics;

namespace VoltTray.Hosting;

/// <summary>
/// Launch configuration for running a single read-only USB HID diagnostic inspection.
/// </summary>
public sealed record InspectLaunchOptions(
    UpsDiagnosticInquiry Inquiry,
    bool IncludeAllHidDevices,
    string? ExportPath) : ApplicationLaunchOptions;
