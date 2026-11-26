using VoltTray.Core.Models;
using VoltTray.Infrastructure.Protocol;
using VoltTray.Shared;

namespace VoltTray.Infrastructure.Diagnostics;

/// <summary>
/// Evaluates raw frame delimiters/escapes, electrical/battery physics invariants,
/// and provenance coercion guards before telemetry reaches ShutdownCoordinator or UI.
/// </summary>
public static class TelemetryPlausibilityGuard
{
    public const double MinMainsInputVoltageAc = 10.0;
    public const double MaxStaticBatteryVoltageDriftDc = 0.25;
    public const double MaxSingleCycleBatteryJumpDc = 6.0;

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

    public static TelemetryAnomalyKind? EvaluatePhysics(UpsTelemetry candidate, UpsTelemetry? lastKnownGood)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (candidate.FrequencyHz < ElectricalConstants.MinPlausibleFrequencyHz ||
            candidate.FrequencyHz > ElectricalConstants.MaxPlausibleFrequencyHz)
        {
            return TelemetryAnomalyKind.PhysicsImplausible;
        }

        bool isOnBattery = candidate.PowerStatus is { } powerStatus
            ? powerStatus.IsOnBattery
            : candidate.State is UpsOperationalState.OnBattery or UpsOperationalState.BatteryLow;

        if (!isOnBattery &&
            candidate.InputVoltageAc < MinMainsInputVoltageAc &&
            lastKnownGood != null &&
            Math.Abs(candidate.BatteryVoltageDc - lastKnownGood.BatteryVoltageDc) <= MaxStaticBatteryVoltageDriftDc)
        {
            return TelemetryAnomalyKind.PhysicsImplausible;
        }

        if (lastKnownGood != null &&
            Math.Abs(candidate.BatteryVoltageDc - lastKnownGood.BatteryVoltageDc) > MaxSingleCycleBatteryJumpDc &&
            candidate.State == lastKnownGood.State)
        {
            return TelemetryAnomalyKind.PhysicsImplausible;
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
