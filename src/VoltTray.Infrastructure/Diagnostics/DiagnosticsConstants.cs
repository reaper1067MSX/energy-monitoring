namespace VoltTray.Infrastructure.Diagnostics;

public static class DiagnosticsConstants
{
    public const int RingBufferCapacity = 256;
    public const int DefaultDumpCount = 16;
    public const int MaxLastKnownGoodHoldCycles = 3;
    public const string DiagnosticsFolderName = "diagnostics";
    public const string ApplicationFolderName = "VoltTray";
    public const string DumpTimestampFormat = "yyyyMMdd-HHmmss-fff";
    public const double MinMainsInputVoltageAc = 10.0;
    public const double MaxStaticBatteryVoltageDriftDc = 0.25;
    public const double MaxSingleCycleBatteryJumpDc = 6.0;
}
