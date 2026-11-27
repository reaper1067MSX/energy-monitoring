namespace VoltTray.Core.History;

public sealed record TelemetryHistorySnapshot(
    double TodayEnergyKwh,
    int SessionPeakWatts,
    double? TodayMinInputAc,
    double? TodayMaxInputAc,
    int TodayAvrCount,
    int TodayOutageCount,
    IReadOnlyList<DailyTelemetrySummary> RecentDays,
    IReadOnlyList<PowerOutageEvent> RecentOutages);
