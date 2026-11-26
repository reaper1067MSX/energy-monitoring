using System.Text;
using Microsoft.Win32.SafeHandles;
using VoltTray.Infrastructure.Protocol;

namespace VoltTray.Infrastructure.Hid;

/// <summary>
/// Frames ASCII UPS commands into 9-byte USB HID output reports and reassembles 0x00-preserving input payloads.
/// </summary>
public static class HidReportFramer
{
    /// <summary>
    /// Splits an ASCII UPS command into 9-byte HID output reports (Report ID 0x00 + up to 8 payload bytes).
    /// </summary>
    public static IReadOnlyList<byte[]> CreateOutputReports(string command)
    {
        ArgumentException.ThrowIfNullOrEmpty(command);

        byte[] commandBytes = Encoding.ASCII.GetBytes(command);
        int chunkLength = HidConstants.PayloadChunkLength;
        List<byte[]> reports = new((commandBytes.Length + chunkLength - 1) / chunkLength);

        for (int offset = 0; offset < commandBytes.Length; offset += chunkLength)
        {
            byte[] report = new byte[HidConstants.ReportLength];
            report[0] = HidConstants.DefaultReportId;
            int copyLength = Math.Min(chunkLength, commandBytes.Length - offset);
            Array.Copy(commandBytes, offset, report, HidConstants.ReportPayloadStartIndex, copyLength);
            reports.Add(report);
        }

        return reports;
    }

    internal static bool WriteCommand(SafeFileHandle handle, string command)
    {
        foreach (byte[] report in CreateOutputReports(command))
        {
            if (!HidOverlappedIo.WriteReport(handle, report, HidConstants.DefaultWriteTimeoutMs))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Reassembles 9-byte HID input reports while preserving every interior 0x00 byte up to 0x0D.
    /// </summary>
    public static byte[] AssembleInputPayload(IEnumerable<byte[]> reports, bool requireFramingHeader = false)
    {
        List<byte> payload = new();

        foreach (byte[] report in reports)
        {
            if (report.Length <= HidConstants.ReportPayloadStartIndex)
            {
                continue;
            }

            if (AppendReportBytes(payload, report.AsSpan(HidConstants.ReportPayloadStartIndex), requireFramingHeader))
            {
                break;
            }
        }

        return payload.ToArray();
    }

    /// <summary>
    /// Appends payload bytes from a single HID report slice and returns true once 0x0D is encountered.
    /// </summary>
    public static bool AppendReportBytes(
        List<byte> payload,
        ReadOnlySpan<byte> reportPayload,
        bool requireFramingHeader = false)
    {
        for (int index = 0; index < reportPayload.Length; index++)
        {
            byte currentByte = reportPayload[index];
            if (ShouldSkipLeadingByte(payload.Count, currentByte, requireFramingHeader))
            {
                continue;
            }

            payload.Add(currentByte);
            if (currentByte == VoltronicProtocolConstants.CarriageReturn)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ShouldSkipLeadingByte(int currentCount, byte candidateByte, bool requireFramingHeader)
    {
        if (currentCount > 0)
        {
            return false;
        }

        if (requireFramingHeader)
        {
            return candidateByte != VoltronicProtocolConstants.FrameHeaderHash &&
                   candidateByte != VoltronicProtocolConstants.FrameHeaderParen;
        }

        return candidateByte == VoltronicProtocolConstants.NullByte;
    }
}
