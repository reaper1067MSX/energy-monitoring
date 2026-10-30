using VoltTray.Core.Ports;

namespace VoltTray.Infrastructure.OS;

/// <summary>
/// Infrastructure adapter for Windows desktop notifications.
/// Dispatches shell toasts or logs notifications to listeners.
/// </summary>
public sealed class WindowsNotificationPort : INotificationPort
{
    public record NotificationRecord(string Title, string Message, NotificationSeverity Severity, DateTime Timestamp);

    private readonly List<NotificationRecord> _history = new();
    private readonly object _lock = new();

    public IReadOnlyList<NotificationRecord> History
    {
        get
        {
            lock (_lock)
            {
                return _history.ToArray();
            }
        }
    }

    public event Action<NotificationRecord>? NotificationDispatched;

    public void ShowNotification(string title, string message, NotificationSeverity severity)
    {
        var record = new NotificationRecord(title, message, severity, DateTime.UtcNow);
        lock (_lock)
        {
            _history.Add(record);
        }

        NotificationDispatched?.Invoke(record);
    }
}
