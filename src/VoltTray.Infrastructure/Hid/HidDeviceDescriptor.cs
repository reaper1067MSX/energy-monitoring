namespace VoltTray.Infrastructure.Hid;

/// <summary>
/// USB HID device interface descriptor metadata discovered via SetupAPI and hid.dll.
/// </summary>
public sealed record HidDeviceDescriptor(
    string DevicePath,
    ushort VendorId,
    ushort ProductId,
    ushort VersionNumber,
    string Manufacturer,
    string Product,
    string SerialNumber,
    ushort InputReportByteLength,
    ushort OutputReportByteLength)
{
    public bool IsSupportedVoltronicDevice =>
        VendorId == HidConstants.VendorId &&
        ProductId == HidConstants.ProductId;
}
