using System.Diagnostics;
using Microsoft.Win32.SafeHandles;
using VoltTray.Core.Models;
using VoltTray.Infrastructure.Protocol;

namespace VoltTray.Infrastructure.Hid;

/// <summary>
/// Executes read-only HID inquiry probes and builds decoded forensic captures.
/// </summary>
public sealed class ReadOnlyUpsProbe
{
    private const int MinimumFrequencyTokenCount = 7;
    private const int FrequencyPeriodTokenIndex = 5;
    private const int FrequencyClockTokenIndex = 6;
    private const int MinimumStatusByte2TokenCount = 11;
    private const int StatusByte2TokenIndex = 10;

    private readonly IHidDeviceScanner _deviceScanner;

    public ReadOnlyUpsProbe(IHidDeviceScanner? deviceScanner = null)
    {
        _deviceScanner = deviceScanner ?? new HidDeviceScanner();
    }

    /// <summary>
    /// Discovers the first matching Voltronic/Forza HID device and executes one read-only inquiry.
    /// </summary>
    public UpsProbeCapture? InspectFirstMatchingDevice(
        UpsDiagnosticInquiry inquiry = UpsDiagnosticInquiry.QuickStatus,
        bool includeAllHidDevices = false)
    {
        HidDeviceDescriptor? targetDevice = _deviceScanner
            .DiscoverDevices(includeAllHidDevices)
            .FirstOrDefault(descriptor =>
                descriptor.VendorId == HidConstants.VendorId &&
                descriptor.ProductId == HidConstants.ProductId);

        if (targetDevice == null)
        {
            return null;
        }

        using SafeFileHandle handle = HidOverlappedIo.OpenDeviceHandle(targetDevice.DevicePath);
        if (handle.IsInvalid)
        {
            return null;
        }

        return QueryReadOnly(handle, targetDevice, inquiry);
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
    /// </summary>
    public static UpsProbeCapture BuildCapture(
        HidDeviceDescriptor device,
        UpsDiagnosticInquiry inquiry,
        IReadOnlyList<byte[]> rawReports,
        byte[] rawPayload)
    {
        string wireCommand = VoltronicInquiryCodec.ToWireCommand(inquiry);

        IReadOnlyList<byte[]> tokens = VoltronicParser.TryTokenizeHashFrame(rawPayload, out IReadOnlyList<byte[]> parsedTokens)
            ? parsedTokens
            : Array.Empty<byte[]>();

        double? frequencyHz = tokens.Count >= MinimumFrequencyTokenCount
            ? VoltronicParser.DecodeFrequencyHz(tokens[FrequencyPeriodTokenIndex], tokens[FrequencyClockTokenIndex])
            : null;

        UpsStatusByte2Ratings? statusByte2Ratings =
            tokens.Count >= MinimumStatusByte2TokenCount && tokens[StatusByte2TokenIndex].Length > 0
                ? VoltronicParser.DecodeStatusByte2Ratings(tokens[StatusByte2TokenIndex][0])
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
