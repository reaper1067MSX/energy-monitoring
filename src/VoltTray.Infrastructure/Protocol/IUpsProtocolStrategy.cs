using VoltTray.Core.Telemetry;

namespace VoltTray.Infrastructure.Protocol;

public interface IUpsProtocolStrategy
{
    string DialectId { get; }
    string StatusQueryCommand { get; }
    UpsTelemetry? Parse(ReadOnlySpan<byte> frame, UpsCapabilities capabilities, UpsTelemetry? previous, DateTimeOffset now);
    void ResetSelfTest();
}
