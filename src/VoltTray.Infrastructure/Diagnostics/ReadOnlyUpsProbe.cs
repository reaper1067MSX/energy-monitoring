using VoltTray.Core.Diagnostics;
using System.Diagnostics;
using Microsoft.Win32.SafeHandles;
using VoltTray.Infrastructure.Hid;
using VoltTray.Infrastructure.Protocol;

namespace VoltTray.Infrastructure.Diagnostics;

/// <summary>
/// Executes read-only HID inquiry probes and builds decoded forensic captures.
/// </summary>
public sealed class ReadOnlyUpsProbe : IUpsDiagnosticProbe
{
    private readonly IHidDeviceScanner _deviceScanner;

    public ReadOnlyUpsProbe(IHidDeviceScanner? deviceScanner = null)
    {
        _deviceScanner = deviceScanner ?? new HidDeviceScanner();
    }

    public IReadOnlyList<HidDeviceDescriptor> DiscoverDevices(bool includeAllHidDevices = false)
    {
        return _deviceScanner.DiscoverDevices(includeAllHidDevices);
    }

    public UpsProbeCapture? QueryDevice(HidDeviceDescriptor device, UpsDiagnosticInquiry inquiry)
    {
        ArgumentNullException.ThrowIfNull(device);

        using SafeFileHandle handle = HidOverlappedIo.OpenDeviceHandle(device.DevicePath);
        if (handle.IsInvalid)
        {
            return null;
        }

        return QueryReadOnly(handle, device, inquiry);
    }

    /// <summary>
    /// Discovers the first matching Voltronic/Forza HID device and executes one read-only inquiry.
    /// </summary>
    public UpsProbeCapture? InspectFirstMatchingDevice(
        UpsDiagnosticInquiry inquiry = UpsDiagnosticInquiry.QuickStatus)
    {
        HidDeviceDescriptor? targetDevice = DiscoverDevices(includeAllHidDevices: false)
            .FirstOrDefault(descriptor => descriptor.IsSupportedVoltronicDevice);

        return targetDevice == null ? null : QueryDevice(targetDevice, inquiry);
    }

    /// <summary>
    /// Executes one read-only inquiry against an open HID handle without stripping interior 0x00 bytes.
    /// </summary>
    public UpsProbeCapture? QueryReadOnly(
        SafeFileHandle handle,
        HidDeviceDescriptor device,
        UpsDiagnosticInquiry inquiry)
    {
        string wireCommand = VoltronicInquiryCodec.ToWireCommand(inquiry);
        HidOverlappedIo.DrainInputBuffer(handle, HidConstants.DefaultDrainTimeoutMs);

        if (!HidReportFramer.WriteCommand(handle, wireCommand))
        {
            return null;
        }

        List<byte[]> rawReports = new();
        byte[] assembledPayload = Array.Empty<byte>();
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.ElapsedMilliseconds < HidConstants.DefaultQueryTimeoutMs)
        {
            byte[] reportBuffer = new byte[HidConstants.ReportLength];
            int bytesRead = HidOverlappedIo.ReadReport(handle, reportBuffer, HidConstants.DefaultReadTimeoutMs);
            if (bytesRead <= 0)
            {
                continue;
            }

            rawReports.Add(reportBuffer);
            assembledPayload = HidReportFramer.AssembleInputPayload(rawReports, requireFramingHeader: false);
            if (assembledPayload.Length > 0 && assembledPayload[^1] == VoltronicProtocolConstants.CarriageReturn)
            {
                break;
            }
        }

        return rawReports.Count == 0
            ? null
            : BuildCapture(device, inquiry, rawReports, assembledPayload);
    }

    /// <summary>
    /// Builds an immutable forensic capture from captured HID reports and assembled payload bytes.
    /// Binary Protocol 'T' decoding is gated strictly to valid <see cref="UpsDiagnosticInquiry.QuickStatus"/> '#' frames.
    /// </summary>
    public static UpsProbeCapture BuildCapture(
        HidDeviceDescriptor device,
        UpsDiagnosticInquiry inquiry,
        IReadOnlyList<byte[]> rawReports,
        byte[] rawPayload)
    {
        string wireCommand = VoltronicInquiryCodec.ToWireCommand(inquiry);

        if (inquiry != UpsDiagnosticInquiry.QuickStatus ||
            !VoltronicParser.TryTokenizeHashFrame(rawPayload, out IReadOnlyList<byte[]> tokens))
        {
            return new UpsProbeCapture(
                device,
                inquiry,
                wireCommand,
                rawReports,
                rawPayload,
                rawPayload,
                Array.Empty<byte[]>(),
                frequencyHz: null,
                statusByte2Ratings: null);
        }

        double? frequencyHz = tokens.Count >= VoltronicProtocolConstants.MinFrequencyTokenCount
            ? VoltronicParser.TryDecodeFrequencyHz(
                tokens[VoltronicProtocolConstants.FrequencyPeriodTokenIndex],
                tokens[VoltronicProtocolConstants.FrequencyClockTokenIndex])
            : null;

        UpsStatusByte2Ratings? statusByte2Ratings =
            tokens.Count >= VoltronicProtocolConstants.MinStatusByte2TokenCount &&
            tokens[VoltronicProtocolConstants.StatusByte2TokenIndex].Length > 0
                ? VoltronicParser.DecodeStatusByte2Ratings(
                    tokens[VoltronicProtocolConstants.StatusByte2TokenIndex][0])
                : null;

        byte[] deescapedPayload = VoltronicParser.Deescape(rawPayload);

        return new UpsProbeCapture(
            device,
            inquiry,
            wireCommand,
            rawReports,
            rawPayload,
            deescapedPayload,
            tokens,
            frequencyHz,
            statusByte2Ratings);
    }
}
