using VoltTray.Core.Models;

namespace VoltTray.Infrastructure.Diagnostics;

public sealed class SingleCycleBatterySpikePhysicsRule : ITelemetryPhysicsRule
{
    public TelemetryAnomalyKind? Evaluate(UpsTelemetry candidate, UpsTelemetry? lastKnownGood)
    {
        if (lastKnownGood != null &&
            Math.Abs(candidate.BatteryVoltageDc - lastKnownGood.BatteryVoltageDc) >
            DiagnosticsConstants.MaxSingleCycleBatteryJumpDc &&
            candidate.State == lastKnownGood.State)
        {
            return TelemetryAnomalyKind.PhysicsImplausible;
        }

        return null;
    }
}
