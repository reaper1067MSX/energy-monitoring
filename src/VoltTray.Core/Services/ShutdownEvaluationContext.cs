namespace VoltTray.Core.Services;

public sealed record ShutdownEvaluationContext(
    bool LowBattery,
    int? RuntimeMinutes,
    int RuntimeThresholdMinutes,
    double? BatteryVoltageDc,
    int SeriesBatteryCount);
