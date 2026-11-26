using VoltTray.Shared;

namespace VoltTray.Infrastructure.Hid;

/// <summary>
/// Executes asynchronous operations with bounded backoff retry strictly on transient errors,
/// aborting immediately when cancelled.
/// </summary>
public sealed class AsyncRetryPolicy
{
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly int _maxAttempts;
    private readonly int[] _retryDelaysMs;
    private readonly int _maxJitterMs;

    public AsyncRetryPolicy(
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        int maxAttempts = HidConstants.MaxConnectAttempts,
        int[]? retryDelaysMs = null,
        int maxJitterMs = HidConstants.ConnectRetryJitterMaxMs)
    {
        _delayAsync = delayAsync ?? Task.Delay;
        _maxAttempts = Math.Max(1, maxAttempts);
        _retryDelaysMs = retryDelaysMs is { Length: > 0 } ? retryDelaysMs : HidConstants.ConnectRetryDelaysMs;
        _maxJitterMs = Math.Max(0, maxJitterMs);
    }

    public async Task<Result<T>> ExecuteAsync<T>(
        Func<CancellationToken, Task<Result<T>>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        Result<T> lastResult = default;

        for (int attemptIndex = 0; attemptIndex < _maxAttempts; attemptIndex++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Result<T>.Failure(CreateCancelledError());
            }

            try
            {
                lastResult = await operation(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Result<T>.Failure(CreateCancelledError());
            }

            if (lastResult.IsSuccess ||
                lastResult.Error is not { IsTransient: true } ||
                attemptIndex + 1 >= _maxAttempts)
            {
                return lastResult;
            }

            int baseDelayMs = _retryDelaysMs[Math.Min(attemptIndex, _retryDelaysMs.Length - 1)];
            int jitterMs = _maxJitterMs > 0 ? Random.Shared.Next(0, _maxJitterMs + 1) : 0;
            TimeSpan delay = TimeSpan.FromMilliseconds(baseDelayMs + jitterMs);

            try
            {
                await _delayAsync(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return Result<T>.Failure(CreateCancelledError());
            }
        }

        return lastResult;
    }

    private static DomainError CreateCancelledError()
    {
        return new DomainError(
            SharedErrorConstants.TransportTimeoutCode,
            "Connection retry cancelled.",
            ErrorCategory.TransportTimeout,
            IsTransient: false);
    }
}
