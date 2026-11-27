namespace VoltTray.Core.History;

public sealed record DailyTelemetrySummary(
    DateOnly Date,
    double EnergyKwh,
    int PeakWatts,
    double? MinInputVoltageAc,
    double? MaxInputVoltageAc,
    int AvrEventCount,
    int OutageCount,
    int BatterySeconds);
