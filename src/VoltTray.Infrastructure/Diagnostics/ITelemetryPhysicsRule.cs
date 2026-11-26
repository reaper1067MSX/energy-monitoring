using VoltTray.Core.Models;

namespace VoltTray.Infrastructure.Diagnostics;

public interface ITelemetryPhysicsRule
{
    TelemetryAnomalyKind? Evaluate(UpsTelemetry candidate, UpsTelemetry? lastKnownGood);
}
