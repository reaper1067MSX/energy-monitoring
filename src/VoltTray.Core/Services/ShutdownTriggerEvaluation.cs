namespace VoltTray.Core.Services;

public readonly record struct ShutdownTriggerEvaluation(
    bool Applies,
    bool IsTriggered,
    bool RequiresDebounce,
    string TriggerReason);
