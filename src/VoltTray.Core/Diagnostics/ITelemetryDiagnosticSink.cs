using VoltTray.Core.Telemetry;

namespace VoltTray.Core.Diagnostics;

public interface ITelemetryDiagnosticSink
{
    int Count { get; }

    void Record(
        ReadOnlySpan<byte> rawFrame,
        UpsTelemetry? domainTelemetry,
        TelemetryAnomalyKind? anomaly = null,
        string? uiSummary = null);

    string? DumpRecent(int count = 16);
}
