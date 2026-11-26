namespace VoltTray.Core.Services;

public interface IShutdownTriggerRule
{
    ShutdownTriggerEvaluation Evaluate(ShutdownEvaluationContext context);
}
