namespace VoltTray.Infrastructure.Hid;

/// <summary>
/// Centralized Win32 USB Human Interface Device (HID) hardware and communication constants.
/// </summary>
public static class HidConstants
{
    // HID Report Buffer Structure
    public const int ReportLength = 9;
    public const int PayloadChunkLength = 8;
    public const int ReportPayloadStartIndex = 1;
    public const byte DefaultReportId = 0x00;

    // Device Identification (Voltronic / Forza UPS)
    public const ushort VendorId = 0x0665;
    public const ushort ProductId = 0x5161;
    public const string SimulatedDevicePath = @"\\?\SIMULATED_UPS_FORZA_FX2200";

    // Communication Timeouts (Milliseconds)
    public const int DefaultDrainTimeoutMs = 25;
    public const int DefaultReadTimeoutMs = 400;
    public const int DefaultWriteTimeoutMs = 1000;
    public const int DefaultQueryTimeoutMs = 1500;

    // Timing Constants (Seconds)
    public const int SelfTestDurationSeconds = 10;
}
