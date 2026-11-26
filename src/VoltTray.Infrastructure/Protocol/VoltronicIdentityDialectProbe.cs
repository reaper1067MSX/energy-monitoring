using VoltTray.Core.Models;
using VoltTray.Core.Ports;

namespace VoltTray.Infrastructure.Protocol;

/// <summary>
/// Probes Voltronic M\r identity and resolves 'T', 'P', or 'V' dialects via factory dispatch map.
/// </summary>
public sealed class VoltronicIdentityDialectProbe : IProtocolDialectProbe
{
    private static readonly IReadOnlyDictionary<
        char,
        Func<Func<string, byte[]?>, UpsCapabilities, IBatteryAutonomyEstimator, NegotiatedUpsProtocol>>
        DialectFactories =
            new Dictionary<
                char,
                Func<Func<string, byte[]?>, UpsCapabilities, IBatteryAutonomyEstimator, NegotiatedUpsProtocol>>
            {
                [VoltronicProtocolConstants.DialectCharT] = (_, capabilities, estimator) => new NegotiatedUpsProtocol(
                    new VoltronicQsHexProtocol(VoltronicProtocolConstants.DialectT, estimator),
                    capabilities with { DialectVerified = true }),
                [VoltronicProtocolConstants.DialectCharP] = (_, capabilities, estimator) => new NegotiatedUpsProtocol(
                    new VoltronicQsHexProtocol(VoltronicProtocolConstants.DialectP, estimator),
                    capabilities with { DialectVerified = true }),
                [VoltronicProtocolConstants.DialectCharV] = (queryRaw, capabilities, estimator) =>
                {
                    UpsCapabilities enriched = MegatecAsciiProtocol.ProbeCapabilities(
                        queryRaw,
                        capabilities with { DialectVerified = true });
                    return new NegotiatedUpsProtocol(
                        new MegatecAsciiProtocol(
                            VoltronicProtocolConstants.DialectV,
                            VoltronicProtocolConstants.QueryStatusCommand,
                            estimator),
                        enriched);
                }
            };

    public NegotiatedUpsProtocol? TryProbe(
        Func<string, byte[]?> queryRaw,
        UpsCapabilities baseCapabilities,
        IBatteryAutonomyEstimator estimator)
    {
        char? dialectChar = ExtractDialectCharacter(queryRaw(VoltronicProtocolConstants.ProtocolIdentityCommand));
        if (dialectChar is char matchedChar &&
            DialectFactories.TryGetValue(matchedChar, out var factory))
        {
            return factory(queryRaw, baseCapabilities, estimator);
        }

        return null;
    }

    private static char? ExtractDialectCharacter(byte[]? response)
    {
        if (response == null)
        {
            return null;
        }

        for (int index = 0; index < response.Length; index++)
        {
            byte value = response[index];
            if (value == VoltronicProtocolConstants.NullByte)
            {
                continue;
            }

            return (char)value;
        }

        return null;
    }
}
