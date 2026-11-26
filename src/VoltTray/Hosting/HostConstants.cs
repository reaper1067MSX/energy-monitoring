namespace VoltTray.Hosting;

public static class HostConstants
{
    public const int ConsecutiveErrorsBeforeCommLost = 3;
    public const int PollingStopTimeoutMs = 500;
    public const int MinimumPauseTimerMs = 1;
    public const int FallbackAnchorSizePx = 32;
    public const int FallbackAnchorOffsetPx = 40;
    public const string DefaultMutexName = @"Global\VoltTray_SingleInstance_Mutex";
    public const string GlobalMutexPrefix = @"Global\";
}
