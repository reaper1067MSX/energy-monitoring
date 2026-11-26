using VoltTray.Core.Models;
using VoltTray.Core.Ports;

namespace VoltTray.Infrastructure.Protocol;

public interface IProtocolDialectProbe
{
    NegotiatedUpsProtocol? TryProbe(
        Func<string, byte[]?> queryRaw,
        UpsCapabilities baseCapabilities,
        IBatteryAutonomyEstimator estimator);
}
