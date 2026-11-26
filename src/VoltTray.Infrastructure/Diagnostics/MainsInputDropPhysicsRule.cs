using VoltTray.Core.Models;

namespace VoltTray.Infrastructure.Diagnostics;

public sealed class MainsInputDropPhysicsRule : ITelemetryPhysicsRule
{
    public TelemetryAnomalyKind? Evaluate(UpsTelemetry candidate, UpsTelemetry? lastKnownGood)
    {
        bool isOnBattery = candidate.PowerStatus is { } powerStatus
            ? powerStatus.IsOnBattery
            : candidate.State is UpsOperationalState.OnBattery or UpsOperationalState.BatteryLow;

        if (!isOnBattery &&
            candidate.InputVoltageAc < TelemetryPlausibilityGuard.MinMainsInputVoltageAc &&
            lastKnownGood != null &&
            Math.Abs(candidate.BatteryVoltageDc - lastKnownGood.BatteryVoltageDc) <=
            TelemetryPlausibilityGuard.MaxStaticBatteryVoltageDriftDc)
        {
            return TelemetryAnomalyKind.PhysicsImplausible;
        }

        return null;
    }
}
