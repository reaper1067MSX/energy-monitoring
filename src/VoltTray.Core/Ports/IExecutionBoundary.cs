using VoltTray.Shared;

namespace VoltTray.Core.Ports;

public interface IExecutionBoundary
{
    Result Execute(Action action, string operationName);

    Task<Result<T>> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken = default);
}
