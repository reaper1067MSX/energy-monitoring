namespace VoltTray.Core.History;

public sealed record PowerOutageEvent(
    DateTimeOffset StartedAt,
    int DurationSeconds,
    double MinBatteryVoltageDc,
    int PreEventAvrCount);
