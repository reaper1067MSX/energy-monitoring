using VoltTray.Core.Models;
using VoltTray.Infrastructure.Protocol;

namespace VoltTray.Infrastructure.Hid;

/// <summary>
/// Immutable forensic capture produced from a read-only USB HID UPS inquiry.
/// </summary>
public sealed record UpsProbeCapture(
    HidDeviceDescriptor Device,
    UpsDiagnosticInquiry Inquiry,
    string WireCommand,
    IReadOnlyList<byte[]> RawReports,
    byte[] RawPayload,
    byte[] DeescapedPayload,
    IReadOnlyList<byte[]> Tokens,
    double? FrequencyHz,
    UpsStatusByte2Ratings? StatusByte2Ratings);
