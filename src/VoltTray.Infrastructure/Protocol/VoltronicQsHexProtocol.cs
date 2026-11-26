using VoltTray.Core.Battery;
using VoltTray.Core.Telemetry;

namespace VoltTray.Infrastructure.Protocol;

/// <summary>
/// Voltronic binary QS-Hex protocol strategy supporting dialects 'T' (11-token + Status Byte 2)
/// and 'P' (10-token without Status Byte 2).
/// </summary>
public sealed class VoltronicQsHexProtocol : IUpsProtocolStrategy
{
    private readonly VoltronicParser _parser;

    public string DialectId { get; }
    public string StatusQueryCommand => VoltronicProtocolConstants.QueryStatusCommand;

    public VoltronicQsHexProtocol(
        string dialectId = VoltronicProtocolConstants.DialectT,
        IBatteryAutonomyEstimator? estimator = null,
        double capacityFactor = 1.0)
    {
        if (dialectId != VoltronicProtocolConstants.DialectT &&
            dialectId != VoltronicProtocolConstants.DialectP)
        {
            throw new ArgumentException($"Unsupported QS-Hex dialect '{dialectId}'.", nameof(dialectId));
        }

        DialectId = dialectId;
        _parser = new VoltronicParser(estimator, capacityFactor);
    }

    public UpsTelemetry? Parse(
        ReadOnlySpan<byte> frame,
        UpsCapabilities capabilities,
        UpsTelemetry? previous,
        DateTimeOffset now)
    {
        return _parser.Parse(frame, capabilities, previous, now);
    }

    public void ResetSelfTest()
    {
        _parser.ResetSelfTest();
    }
}
