namespace VoltTray.Core.Safety;

public interface IShutdownTriggerRule
{
    ShutdownTriggerEvaluation Evaluate(ShutdownEvaluationContext context);
}
