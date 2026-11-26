namespace VoltTray.Infrastructure.Hid;

public interface IRawTelemetryPort
{
    Task<RawUpsTelemetrySample?> ReadRawTelemetryAsync(
        DateTimeOffset? now = null,
        CancellationToken cancellationToken = default);
}
