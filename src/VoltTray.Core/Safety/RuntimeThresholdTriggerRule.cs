namespace VoltTray.Core.Safety;

public sealed class RuntimeThresholdTriggerRule : IShutdownTriggerRule
{
    public ShutdownTriggerEvaluation Evaluate(ShutdownEvaluationContext context)
    {
        if (context.RuntimeMinutes is not int measuredRuntime)
        {
            return default;
        }

        return new ShutdownTriggerEvaluation(
            Applies: true,
            IsTriggered: measuredRuntime <= context.RuntimeThresholdMinutes,
            RequiresDebounce: true,
            TriggerReason: $"Estimated runtime depleted ({measuredRuntime} min <= {context.RuntimeThresholdMinutes} min)");
    }
}
