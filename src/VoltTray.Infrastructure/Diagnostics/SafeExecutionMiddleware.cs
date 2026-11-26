using VoltTray.Core.Models;
using VoltTray.Core.Ports;
using VoltTray.Shared;

namespace VoltTray.Infrastructure.Diagnostics;

/// <summary>
/// Captures unexpected system exceptions at execution boundaries, converts them into
/// structured DomainError results, and flushes diagnostics to the telemetry sink.
/// </summary>
public sealed class SafeExecutionMiddleware : IExecutionBoundary
{
    private readonly ITelemetryDiagnosticSink? _diagnosticSink;

    public SafeExecutionMiddleware(ITelemetryDiagnosticSink? diagnosticSink = null)
    {
        _diagnosticSink = diagnosticSink;
    }

    public Result Execute(Action action, string operationName)
    {
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            action();
            return Result.Success();
        }
        catch (Exception exception)
        {
            RecordUnexpectedException(operationName, exception);
            return Result.Failure(CreateUnexpectedError(operationName, exception));
        }
    }

    public async Task<Result<T>> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        try
        {
            T result = await operation(cancellationToken).ConfigureAwait(false);
            return Result<T>.Success(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            RecordUnexpectedException(operationName, exception);
            return Result<T>.Failure(CreateUnexpectedError(operationName, exception));
        }
    }

    private void RecordUnexpectedException(string operationName, Exception exception)
    {
        if (_diagnosticSink is null)
        {
            return;
        }

        string summary = $"{operationName}: {exception.GetType().Name}: {exception.Message}";
        _diagnosticSink.Record(
            ReadOnlySpan<byte>.Empty,
            domainTelemetry: null,
            anomaly: TelemetryAnomalyKind.SystemException,
            uiSummary: summary);
        _diagnosticSink.DumpRecent();
    }

    private static DomainError CreateUnexpectedError(string operationName, Exception exception)
    {
        return new DomainError(
            SharedErrorConstants.UnexpectedSystemCode,
            $"{operationName} failed: {exception.Message}",
            ErrorCategory.UnexpectedSystem,
            IsTransient: false);
    }
}
