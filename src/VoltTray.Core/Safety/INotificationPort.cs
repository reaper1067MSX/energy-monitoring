namespace VoltTray.Core.Safety;

public enum NotificationSeverity { Info, Warning, Critical }

public interface INotificationPort
{
    void ShowNotification(string title, string message, NotificationSeverity severity);
}
