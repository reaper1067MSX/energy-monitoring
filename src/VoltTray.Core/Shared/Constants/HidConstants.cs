namespace VoltTray.Core.Shared.Constants;

/// <summary>
/// Centralized Win32 USB Human Interface Device (HID) hardware and communication constants.
/// </summary>
public static class HidConstants
{
    // HID Report Buffer Structure
    public const int ReportLength = 9; // 1 byte ReportId + 8 bytes payload chunk
    public const int PayloadChunkLength = 8;
    public const byte DefaultReportId = 0x00;

    // Device Identification (Forza FX-2200 LCD)
    public const ushort VendorId = 0x0665;
    public const ushort ProductId = 0x5161;
    public const string DeviceIdSubstring = "VID_0665&PID_5161";
    public const string HidInterfaceGuid = "{4d1e55b2-f16f-11cf-88cb-001111000030}";
    public const string DefaultDevicePath = @"\\?\HID#VID_0665&PID_5161#7&1949aaec&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";
    public const string SimulatedDevicePath = @"\\?\SIMULATED_UPS_FORZA_FX2200";

    // Communication Timeouts (Milliseconds)
    public const int DefaultDrainTimeoutMs = 25;
    public const int DefaultReadTimeoutMs = 200;
    public const int DefaultWriteTimeoutMs = 1000;
    public const int DefaultQueryTimeoutMs = 1500;

    // Timing Constants (Seconds)
    public const int SelfTestDurationSeconds = 10;
}
