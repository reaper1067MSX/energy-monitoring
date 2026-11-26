using VoltTray.Core.Battery;
using VoltTray.Core.Telemetry;

namespace VoltTray.Infrastructure.Protocol;

public interface IProtocolDialectProbe
{
    NegotiatedUpsProtocol? TryProbe(
        Func<string, byte[]?> queryRaw,
        UpsCapabilities baseCapabilities,
        IBatteryAutonomyEstimator estimator);
}
