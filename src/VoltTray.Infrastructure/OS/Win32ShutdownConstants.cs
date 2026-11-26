namespace VoltTray.Infrastructure.OS;

internal static class Win32ShutdownConstants
{
    public const uint MajorPower = 0x00060000;
    public const uint MinorEnvironment = 0x0000000c;
    public const uint FlagPlanned = 0x80000000;
    public const uint DefaultTimeoutSeconds = 30;
}
