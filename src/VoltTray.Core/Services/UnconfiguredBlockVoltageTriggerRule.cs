namespace VoltTray.Core.Services;

public sealed class UnconfiguredBlockVoltageTriggerRule : IShutdownTriggerRule
{
    public ShutdownTriggerEvaluation Evaluate(ShutdownEvaluationContext context)
    {
        if (context.RuntimeMinutes is not null || context.BatteryVoltageDc is not double measuredVoltageDc)
        {
            return default;
        }

        int normalizedSeriesCount = Math.Max(1, context.SeriesBatteryCount);
        double blockVoltageDc = measuredVoltageDc / normalizedSeriesCount;

        return new ShutdownTriggerEvaluation(
            Applies: true,
            IsTriggered: blockVoltageDc <= ShutdownCoordinator.CriticalBlockVoltageCutoffDc,
            RequiresDebounce: true,
            TriggerReason:
            $"Battery block voltage reached cutoff ({blockVoltageDc:F2} V <= {ShutdownCoordinator.CriticalBlockVoltageCutoffDc:F1} V)");
    }
}
