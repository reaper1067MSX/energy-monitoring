using VoltTray.Core.Telemetry;

namespace VoltTray.Infrastructure.Hid;

public sealed record RawUpsTelemetrySample(byte[] RawFrame, UpsTelemetry? Telemetry);
