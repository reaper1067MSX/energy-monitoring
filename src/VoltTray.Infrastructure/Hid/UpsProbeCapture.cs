using VoltTray.Infrastructure.Protocol;

namespace VoltTray.Infrastructure.Hid;

/// <summary>
/// Immutable forensic capture produced from a read-only USB HID UPS inquiry.
/// </summary>
public sealed record UpsProbeCapture(
    HidDeviceDescriptor Device,
    string Command,
    IReadOnlyList<byte[]> RawReports,
    byte[] RawPayload,
    byte[] DeescapedPayload,
    IReadOnlyList<byte[]> Tokens,
    double? FrequencyHz,
    UpsStatusByte2Ratings? StatusByte2Ratings);
