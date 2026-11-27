using VoltTray.Core.Telemetry;

namespace VoltTray.Infrastructure.Diagnostics;

internal sealed class SingleCycleBatterySpikePhysicsRule : ITelemetryPhysicsRule
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
