using VoltTray.Core.Diagnostics;
using VoltTray.Infrastructure.Hid;
using VoltTray.Infrastructure.Protocol;

namespace VoltTray.Infrastructure.Diagnostics;

/// <summary>
/// Immutable forensic capture produced from a read-only USB HID UPS inquiry.
/// </summary>
public sealed record UpsProbeCapture
{
    public HidDeviceDescriptor Device { get; }
    public UpsDiagnosticInquiry Inquiry { get; }
    public string WireCommand { get; }
    public IReadOnlyList<byte[]> RawReports { get; }
    public byte[] RawPayload { get; }
    public byte[] DeescapedPayload { get; }
    public IReadOnlyList<byte[]> Tokens { get; }
    public double? FrequencyHz { get; }
    public UpsStatusByte2Ratings? StatusByte2Ratings { get; }

    public UpsProbeCapture(
        HidDeviceDescriptor device,
        UpsDiagnosticInquiry inquiry,
        string wireCommand,
        IReadOnlyList<byte[]> rawReports,
        byte[] rawPayload,
        byte[] deescapedPayload,
        IReadOnlyList<byte[]> tokens,
        double? frequencyHz,
        UpsStatusByte2Ratings? statusByte2Ratings)
    {
        Device = device;
        Inquiry = inquiry;
        WireCommand = wireCommand;
        RawReports = CloneByteArrays(rawReports);
        RawPayload = (byte[])rawPayload.Clone();
        DeescapedPayload = (byte[])deescapedPayload.Clone();
        Tokens = CloneByteArrays(tokens);
        FrequencyHz = frequencyHz;
        StatusByte2Ratings = statusByte2Ratings;
    }

    private static IReadOnlyList<byte[]> CloneByteArrays(IReadOnlyList<byte[]> source)
    {
        if (source.Count == 0)
        {
            return Array.Empty<byte[]>();
        }

        byte[][] copies = new byte[source.Count][];
        for (int index = 0; index < source.Count; index++)
        {
            copies[index] = (byte[])source[index].Clone();
        }

        return copies;
    }
}
