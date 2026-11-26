using VoltTray.Core.Telemetry;
using VoltTray.Core.Safety;
using VoltTray.Infrastructure.Protocol;

namespace VoltTray.Infrastructure.Diagnostics;

/// <summary>
/// Evaluates raw frame delimiters/escapes, composable electrical/battery physics rules,
/// and provenance coercion guards before telemetry reaches ShutdownCoordinator or UI.
/// </summary>
public static class TelemetryPlausibilityGuard
{
    private static readonly IReadOnlyList<ITelemetryPhysicsRule> DefaultPhysicsRules =
    [
        new FrequencyBoundsPhysicsRule(),
        new MainsInputDropPhysicsRule(),
        new SingleCycleBatterySpikePhysicsRule()
    ];

    public static TelemetryAnomalyKind? EvaluateFraming(ReadOnlySpan<byte> rawFrame)
    {
        int startIndex = 0;
        while (startIndex < rawFrame.Length && rawFrame[startIndex] == VoltronicProtocolConstants.NullByte)
        {
            startIndex++;
        }

        if (startIndex >= rawFrame.Length)
        {
            return TelemetryAnomalyKind.FramingError;
        }

        byte headerByte = rawFrame[startIndex];
        if (headerByte != VoltronicProtocolConstants.FrameHeaderHash &&
            headerByte != VoltronicProtocolConstants.FrameHeaderParen)
        {
            return TelemetryAnomalyKind.FramingError;
        }

        int relativeCrIndex = rawFrame[(startIndex + 1)..].IndexOf(VoltronicProtocolConstants.CarriageReturn);
        if (relativeCrIndex <= 0)
        {
            return TelemetryAnomalyKind.FramingError;
        }

        int crIndex = startIndex + 1 + relativeCrIndex;
        if (headerByte == VoltronicProtocolConstants.FrameHeaderHash)
        {
            for (int index = startIndex + 1; index < crIndex; index++)
            {
                if (rawFrame[index] != VoltronicProtocolConstants.FrameHeaderParen)
                {
                    continue;
                }

                if (index + 1 >= crIndex || !IsValidBinaryEscapeCode(rawFrame[index + 1]))
                {
                    return TelemetryAnomalyKind.FramingError;
                }

                index++;
            }
        }

        return null;
    }

    public static TelemetryAnomalyKind? EvaluatePhysics(
        UpsTelemetry candidate,
        UpsTelemetry? lastKnownGood,
        IReadOnlyList<ITelemetryPhysicsRule>? rules = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        IReadOnlyList<ITelemetryPhysicsRule> activeRules = rules ?? DefaultPhysicsRules;
        foreach (ITelemetryPhysicsRule rule in activeRules)
        {
            TelemetryAnomalyKind? anomaly = rule.Evaluate(candidate, lastKnownGood);
            if (anomaly != null)
            {
                return anomaly;
            }
        }

        return null;
    }

    public static TelemetryAnomalyKind? EvaluateProvenanceDrift(
        UpsTelemetry candidate,
        MetricProvenance temperatureProvenance,
        double? renderedTemperatureCelsius = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (temperatureProvenance == MetricProvenance.Unavailable &&
            (candidate.TemperatureCelsius != null || renderedTemperatureCelsius != null))
        {
            return TelemetryAnomalyKind.UiDrift;
        }

        return null;
    }

    private static bool IsValidBinaryEscapeCode(byte escapeCode)
    {
        return escapeCode is
            VoltronicProtocolConstants.EscapeCodeCr or
            VoltronicProtocolConstants.EscapeCodeDc1 or
            VoltronicProtocolConstants.EscapeCodeDc3 or
            VoltronicProtocolConstants.EscapeCodeLf or
            VoltronicProtocolConstants.EscapeCodeSpace;
    }
}
