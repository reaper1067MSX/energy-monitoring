namespace VoltTray.Hosting;

public static class CliConstants
{
    public const string FlagPrefix = "--";
    public const string InspectFlag = "--inspect";
    public const string SimulateFlag = "--simulate";
    public const string SimFlag = "--sim";
    public const string MockFlag = "--mock";
    public const string DryRunFlag = "--dry-run";
    public const string IntervalFlag = "--interval";
    public const string TaskbarPillFlag = "--taskbar-pill";
    public const string AllHidFlag = "--all-hid";
    public const string CommandFlag = "--cmd";
    public const string ExportFlag = "--export";
    public const char PrintableAsciiMin = ' ';
    public const char PrintableAsciiMax = '~';
    public const string SafeCaptureGuidance =
        "Safe capture: wall-powered PC, uninterrupted PE ground, upstream Line (L) cut only, 25-60W resistive load, <=10s outage window.";
}
