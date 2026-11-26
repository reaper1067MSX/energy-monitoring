using VoltTray.Core.Models;
using VoltTray.Core.Ports;
using VoltTray.Core.Services;

namespace VoltTray.Infrastructure.Protocol;

/// <summary>
/// Deterministically selects between Voltronic QS-Hex ('T'/'P') and Megatec ASCII ('V'/'Q1')
/// strategies via an ordered <see cref="IProtocolDialectProbe"/> chain.
/// </summary>
public sealed class ProtocolNegotiator
{
    private static readonly IReadOnlyList<IProtocolDialectProbe> DefaultProbes =
    [
        new VoltronicIdentityDialectProbe(),
        new MegatecQ1DialectProbe()
    ];

    private readonly IReadOnlyList<IProtocolDialectProbe> _probes;

    public ProtocolNegotiator(IReadOnlyList<IProtocolDialectProbe>? probes = null)
    {
        _probes = probes ?? DefaultProbes;
    }

    public NegotiatedUpsProtocol Negotiate(
        Func<string, byte[]?> queryRaw,
        IBatteryAutonomyEstimator? estimator = null)
    {
        ArgumentNullException.ThrowIfNull(queryRaw);

        IBatteryAutonomyEstimator resolvedEstimator = estimator ?? new PeukertEstimator();
        var baseCapabilities = new UpsCapabilities(
            ModelName: null,
            NominalVoltageAc: null,
            NominalFrequencyHz: null,
            RatedWatts: null,
            Bank: new BatteryBankSpec(),
            DialectVerified: false);

        foreach (IProtocolDialectProbe probe in _probes)
        {
            NegotiatedUpsProtocol? result = probe.TryProbe(queryRaw, baseCapabilities, resolvedEstimator);
            if (result != null)
            {
                return result;
            }
        }

        return new NegotiatedUpsProtocol(
            new VoltronicQsHexProtocol(VoltronicProtocolConstants.DialectT, resolvedEstimator),
            baseCapabilities);
    }
}
