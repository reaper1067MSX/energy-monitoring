namespace VoltTray.Core.Telemetry;

public sealed record TelemetryGuardOutcome(
    UpsTelemetry? EffectiveTelemetry,
    TelemetryAnomalyKind? Anomaly,
    bool IsQuarantined,
    bool UsedLastKnownGood,
    string? DiagnosticDumpPath);
