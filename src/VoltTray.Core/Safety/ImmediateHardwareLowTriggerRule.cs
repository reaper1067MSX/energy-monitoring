namespace VoltTray.Core.Safety;

public sealed class ImmediateHardwareLowTriggerRule : IShutdownTriggerRule
{
    public ShutdownTriggerEvaluation Evaluate(ShutdownEvaluationContext context)
    {
        if (!context.LowBattery)
        {
            return default;
        }

        return new ShutdownTriggerEvaluation(
            Applies: true,
            IsTriggered: true,
            RequiresDebounce: false,
            TriggerReason: "Critical low battery detected during outage");
    }
}
