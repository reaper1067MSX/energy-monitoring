using VoltTray.Core.Telemetry;

namespace VoltTray.Core.History;

public interface ITelemetryHistoryTracker
{
    TelemetryHistorySnapshot RecordSample(UpsTelemetry telemetry);
    TelemetryHistorySnapshot GetSnapshot();
    void Flush();
}
