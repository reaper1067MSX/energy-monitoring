namespace VoltTray.Core.Safety;

public sealed record ShutdownEvaluationContext(
    bool LowBattery,
    int? RuntimeMinutes,
    int RuntimeThresholdMinutes,
    double? BatteryVoltageDc,
    int SeriesBatteryCount);
