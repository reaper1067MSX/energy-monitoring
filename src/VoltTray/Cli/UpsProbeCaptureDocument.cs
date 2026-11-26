using VoltTray.Infrastructure.Hid;
using VoltTray.Infrastructure.Protocol;

namespace VoltTray.Cli;

/// <summary>
/// Source-generated JSON serialization model for exported UPS diagnostic captures.
/// </summary>
public sealed record UpsProbeCaptureDocument(
    HidDeviceDescriptor Device,
    string Inquiry,
    string WireCommand,
    string RawHex,
    string DeescapedHex,
    string AsciiPreview,
    IReadOnlyList<string> TokenHexBreakdown,
    double? FrequencyHz,
    UpsStatusByte2Ratings? StatusByte2Ratings,
    string SafeCaptureNotice);
