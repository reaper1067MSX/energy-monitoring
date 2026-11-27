using VoltTray.Core.Telemetry;

namespace VoltTray.Infrastructure.Diagnostics;

internal sealed class MainsInputDropPhysicsRule : ITelemetryPhysicsRule
{
    public TelemetryAnomalyKind? Evaluate(UpsTelemetry candidate, UpsTelemetry? lastKnownGood)
    {
        bool isOnBattery = candidate.PowerStatus is { } powerStatus
            ? powerStatus.IsOnBattery
            : candidate.State is UpsOperationalState.OnBattery or UpsOperationalState.BatteryLow;

        if (!isOnBattery &&
            candidate.InputVoltageAc < DiagnosticsConstants.MinMainsInputVoltageAc &&
            lastKnownGood != null &&
            Math.Abs(candidate.BatteryVoltageDc - lastKnownGood.BatteryVoltageDc) <=
            DiagnosticsConstants.MaxStaticBatteryVoltageDriftDc)
        {
            return TelemetryAnomalyKind.PhysicsImplausible;
        }

        return null;
    }
}
