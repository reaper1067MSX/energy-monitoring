namespace VoltTray.Infrastructure.Diagnostics;

public sealed record TelemetryFlightRecorderEntry(
    DateTimeOffset TimestampUtc,
    string RawHex,
    double? InputVoltageAc,
    double? OutputVoltageAc,
    double? FrequencyHz,
    double? BatteryVoltageDc,
    double? TemperatureCelsius,
    int? EstimatedRuntimeMinutes,
    string? State,
    string? Anomaly,
    string? UiSummary);
