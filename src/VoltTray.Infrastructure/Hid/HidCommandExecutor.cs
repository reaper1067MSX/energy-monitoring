namespace VoltTray.Infrastructure.Hid;

/// <summary>
/// Serializes HID command writes and overlapped frame reads under a single SemaphoreSlim gate
/// with cancellation-safe connection disconnection on write/I/O failures.
/// </summary>
internal sealed class HidCommandExecutor : IDisposable
{
    private readonly SemaphoreSlim _ioGate = new(1, 1);

    public async Task<TResult> ExecuteSerializedAsync<TResult>(
        IHidConnection connection,
        CancellationToken linkedToken,
        long generation,
        string command,
        TResult fallbackResult,
        Action disconnectOnError,
        Func<IHidConnection, CancellationToken, long, TResult> executeAfterWrite)
    {
        try
        {
            await _ioGate.WaitAsync(linkedToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
        {
            return fallbackResult;
        }

        try
        {
            return await Task.Run(() =>
            {
                if (linkedToken.IsCancellationRequested || !connection.IsOpen)
                {
                    return fallbackResult;
                }

                try
                {
                    connection.DrainInputBuffer(HidConstants.DefaultDrainTimeoutMs);
                    if (linkedToken.IsCancellationRequested || !connection.WriteCommand(command))
                    {
                        if (!linkedToken.IsCancellationRequested)
                        {
                            disconnectOnError();
                        }

                        return fallbackResult;
                    }

                    return executeAfterWrite(connection, linkedToken, generation);
                }
                catch
                {
                    if (!linkedToken.IsCancellationRequested)
                    {
                        disconnectOnError();
                    }

                    return fallbackResult;
                }
            }, linkedToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return fallbackResult;
        }
        finally
        {
            ReleaseIoGateSafely();
        }
    }

    public static byte[]? QuerySynchronously(IHidConnection connection, string command)
    {
        if (!connection.IsOpen || !connection.WriteCommand(command))
        {
            return null;
        }

        return HidReportFramer.ReadFramedResponse(connection, command, CancellationToken.None);
    }

    public void Dispose()
    {
        _ioGate.Dispose();
    }

    private void ReleaseIoGateSafely()
    {
        try
        {
            _ioGate.Release();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
