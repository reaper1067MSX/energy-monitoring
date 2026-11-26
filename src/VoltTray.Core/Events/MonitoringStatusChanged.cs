using VoltTray.Core.Telemetry;

namespace VoltTray.Core.Events;

/// <summary>
/// Published when monitoring has no telemetry to display
/// (for example paused or communications lost).
/// </summary>
public sealed record MonitoringStatusChanged(UpsOperationalState State);
