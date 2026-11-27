using VoltTray.Core.Battery;
using VoltTray.Core.Telemetry;

namespace VoltTray.Infrastructure.Protocol;

/// <summary>
/// Probes legacy Megatec ASCII devices via Q1\r status framing and F\r / I\r capability enrichment.
/// </summary>
internal sealed class MegatecQ1DialectProbe : IProtocolDialectProbe
{
    public NegotiatedUpsProtocol? TryProbe(
        Func<string, byte[]?> queryRaw,
        UpsCapabilities baseCapabilities,
        IBatteryAutonomyEstimator estimator)
    {
        byte[]? q1Response = queryRaw(VoltronicProtocolConstants.StandardAsciiStatusCommand);
        if (q1Response == null || !MegatecAsciiProtocol.IsValidMegatecStatusFrame(q1Response))
        {
            return null;
        }

        UpsCapabilities enrichedCapabilities = MegatecAsciiProtocol.ProbeCapabilities(
            queryRaw,
            baseCapabilities with { DialectVerified = true });

        return new NegotiatedUpsProtocol(
            new MegatecAsciiProtocol(
                VoltronicProtocolConstants.DialectQ1,
                VoltronicProtocolConstants.StandardAsciiStatusCommand,
                estimator),
            enrichedCapabilities);
    }
}
