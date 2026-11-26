using VoltTray.Core.Models;

namespace VoltTray.Infrastructure.Hid;

public sealed record RawUpsTelemetrySample(byte[] RawFrame, UpsTelemetry? Telemetry);
