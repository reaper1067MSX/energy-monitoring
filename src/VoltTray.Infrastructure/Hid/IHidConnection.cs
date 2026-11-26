namespace VoltTray.Infrastructure.Hid;

/// <summary>
/// Represents an open HID I/O connection supporting timed report transfers and immediate cancellation.
/// </summary>
internal interface IHidConnection : IDisposable
{
    bool IsOpen { get; }
    void CancelPendingIo();
    void DrainInputBuffer(int drainTimeoutMs);
    bool WriteCommand(string command);
    int ReadReport(byte[] buffer, int timeoutMs);
}
