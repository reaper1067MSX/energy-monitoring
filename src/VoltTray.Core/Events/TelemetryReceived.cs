using VoltTray.Core.Telemetry;

namespace VoltTray.Core.Events;

/// <summary>Published after a plausible telemetry sample has been accepted.</summary>
public sealed record TelemetryReceived(UpsTelemetry Telemetry, DateTimeOffset Timestamp);
