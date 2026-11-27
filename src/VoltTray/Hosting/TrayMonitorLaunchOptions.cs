using VoltTray.Core.Telemetry;

namespace VoltTray.Hosting;

/// <summary>
/// Launch configuration for running the background Windows system-tray UPS monitor.
/// </summary>
public sealed record TrayMonitorLaunchOptions(
    bool IsSimulation,
    bool IsDryRun,
    int PollingIntervalMs,
    bool EnableTaskbarPill = false,
    UpsModelPreset? SelectedModel = null,
    TrayIconMode? SelectedIconMode = null) : ApplicationLaunchOptions;
