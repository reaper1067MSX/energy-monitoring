namespace VoltTray.Core.History;

public interface ITelemetryHistoryStore
{
    void UpsertDailySummary(DailyTelemetrySummary summary);
    void RecordOutageEvent(PowerOutageEvent outageEvent);
    IReadOnlyList<DailyTelemetrySummary> LoadRecentDays(int days);
    IReadOnlyList<PowerOutageEvent> LoadRecentOutages(int maxCount);
}
