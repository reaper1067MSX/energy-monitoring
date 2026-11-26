namespace VoltTray.Hosting;

public static class HostConstants
{
    public const int ConsecutiveErrorsBeforeCommLost = 3;
    public const int PollingStopTimeoutMs = 500;
    public const int MaxReconnectBackoffMs = 5000;
    public const int BackoffMultiplier = 2;
    public const int MinimumPauseTimerMs = 1;
    public const int FallbackAnchorSizePx = 32;
    public const int FallbackAnchorOffsetPx = 40;
    public const int UnknownBatteryPercentage = 0;
    public const string ToggleBeeperOperation = "HostLifecycle.ToggleBeeper";
    public const string TriggerSelfTestOperation = "HostLifecycle.TriggerSelfTest";
    public const string DefaultMutexName = @"Global\VoltTray_SingleInstance_Mutex";
    public const string GlobalMutexPrefix = @"Global\";
}
