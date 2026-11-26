using VoltTray.Core.Telemetry;
using VoltTray.Shared;

namespace VoltTray.Infrastructure.Diagnostics;

public sealed class FrequencyBoundsPhysicsRule : ITelemetryPhysicsRule
{
    public TelemetryAnomalyKind? Evaluate(UpsTelemetry candidate, UpsTelemetry? lastKnownGood)
    {
        if (candidate.FrequencyHz < ElectricalConstants.MinPlausibleFrequencyHz ||
            candidate.FrequencyHz > ElectricalConstants.MaxPlausibleFrequencyHz)
        {
            return TelemetryAnomalyKind.PhysicsImplausible;
        }

        return null;
    }
}
