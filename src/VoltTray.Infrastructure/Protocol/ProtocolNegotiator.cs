using VoltTray.Core.Models;
using VoltTray.Core.Ports;
using VoltTray.Core.Services;

namespace VoltTray.Infrastructure.Protocol;

/// <summary>
/// Deterministically selects between Voltronic QS-Hex ('T'/'P') and Megatec ASCII ('V'/'Q1')
/// strategies using M\r and Q1\r probes with optional F\r / I\r capability enrichment.
/// </summary>
public sealed class ProtocolNegotiator
{
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

        char? dialectChar = ExtractDialectCharacter(queryRaw(VoltronicProtocolConstants.ProtocolIdentityCommand));
        if (dialectChar == 'T')
        {
            return new NegotiatedUpsProtocol(
                new VoltronicQsHexProtocol(VoltronicProtocolConstants.DialectT, resolvedEstimator),
                baseCapabilities with { DialectVerified = true });
        }

        if (dialectChar == 'P')
        {
            return new NegotiatedUpsProtocol(
                new VoltronicQsHexProtocol(VoltronicProtocolConstants.DialectP, resolvedEstimator),
                baseCapabilities with { DialectVerified = true });
        }

        if (dialectChar == 'V')
        {
            UpsCapabilities enrichedCapabilities = ProbeMegatecCapabilities(
                queryRaw,
                baseCapabilities with { DialectVerified = true });
            return new NegotiatedUpsProtocol(
                new MegatecAsciiProtocol(
                    VoltronicProtocolConstants.DialectV,
                    VoltronicProtocolConstants.QueryStatusCommand,
                    resolvedEstimator),
                enrichedCapabilities);
        }

        byte[]? q1Response = queryRaw(VoltronicProtocolConstants.StandardAsciiStatusCommand);
        if (q1Response != null && MegatecAsciiProtocol.IsValidMegatecStatusFrame(q1Response))
        {
            UpsCapabilities enrichedCapabilities = ProbeMegatecCapabilities(
                queryRaw,
                baseCapabilities with { DialectVerified = true });
            return new NegotiatedUpsProtocol(
                new MegatecAsciiProtocol(
                    VoltronicProtocolConstants.DialectQ1,
                    VoltronicProtocolConstants.StandardAsciiStatusCommand,
                    resolvedEstimator),
                enrichedCapabilities);
        }

        return new NegotiatedUpsProtocol(
            new VoltronicQsHexProtocol(VoltronicProtocolConstants.DialectT, resolvedEstimator),
            baseCapabilities);
    }

    private static UpsCapabilities ProbeMegatecCapabilities(
        Func<string, byte[]?> queryRaw,
        UpsCapabilities capabilities)
    {
        byte[]? ratingsResponse = queryRaw(VoltronicProtocolConstants.NominalRatingsCommand);
        if (ratingsResponse != null &&
            MegatecAsciiProtocol.TryParseRatingsResponse(ratingsResponse, capabilities, out UpsCapabilities withRatings))
        {
            capabilities = withRatings;
        }

        byte[]? vendorResponse = queryRaw(VoltronicProtocolConstants.VendorInformationCommand);
        if (vendorResponse != null &&
            MegatecAsciiProtocol.TryParseVendorInfoResponse(vendorResponse, capabilities, out UpsCapabilities withVendor))
        {
            capabilities = withVendor;
        }

        return capabilities;
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

            char candidate = (char)value;
            if (candidate is 'T' or 'P' or 'V')
            {
                return candidate;
            }

            return null;
        }

        return null;
    }
}
