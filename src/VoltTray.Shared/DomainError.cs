namespace VoltTray.Shared;

public sealed record DomainError(
    string Code,
    string Message,
    ErrorCategory Category,
    bool IsTransient = false);
