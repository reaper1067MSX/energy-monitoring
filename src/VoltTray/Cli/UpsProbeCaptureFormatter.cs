using System.Text.Json;
using VoltTray.Infrastructure.Hid;
using VoltTray.Infrastructure.Protocol;

namespace VoltTray.Cli;

/// <summary>
/// Formats HID descriptors, forensic capture payloads, and JSON test vectors for <c>--inspect</c>.
/// </summary>
public static class UpsProbeCaptureFormatter
{
    private const char PrintableAsciiMin = ' ';
    private const char PrintableAsciiMax = '~';

    public const string SafeCaptureGuidance =
        "Safe capture (--stream): wall-powered PC, uninterrupted PE, upstream Line (L) cut, 25-60W resistive load, <=10s outage window.";

    public static void WriteDeviceDescriptors(IReadOnlyList<HidDeviceDescriptor> devices, TextWriter output)
    {
        foreach (HidDeviceDescriptor descriptor in devices)
        {
            string serial = string.IsNullOrEmpty(descriptor.SerialNumber) ? "(none)" : descriptor.SerialNumber;
            output.WriteLine(
                $"VID_{descriptor.VendorId:X4}&PID_{descriptor.ProductId:X4} v{descriptor.VersionNumber:X4} | " +
                $"{descriptor.Manufacturer} {descriptor.Product} | Serial: {serial} | " +
                $"In:{descriptor.InputReportByteLength} Out:{descriptor.OutputReportByteLength}");
        }
    }

    public static void WriteCaptureDetails(UpsProbeCapture capture, TextWriter output)
    {
        for (int index = 0; index < capture.RawReports.Count; index++)
        {
            output.WriteLine($"R#{index}: {FormatHex(capture.RawReports[index])}");
        }

        output.WriteLine($"HEX: {FormatHex(capture.RawPayload)}");
        output.WriteLine($"Deescaped: {FormatHex(capture.DeescapedPayload)}");
        output.WriteLine($"ASCII: {FormatAsciiPreview(capture.RawPayload)}");

        foreach (string tokenLine in FormatTokenHexBreakdown(capture.Tokens))
        {
            output.WriteLine(tokenLine);
        }

        if (capture.FrequencyHz is { } frequencyHz)
        {
            output.WriteLine($"Frequency: {frequencyHz:F2} Hz");
        }

        if (capture.StatusByte2Ratings is { } ratings)
        {
            string nominalAcText = ratings.NominalOutputVoltageAc?.ToString() ?? "Unknown";
            output.WriteLine(
                $"Status2: {ratings.NominalFrequencyHz} Hz, {ratings.NominalBatteryVoltageDc} V " +
                $"({ratings.BatteryBankSeriesCount}x12V), {nominalAcText} V");
        }
    }

    public static string SerializeJson(UpsProbeCapture capture)
    {
        var payload = new
        {
            capture.Device,
            Inquiry = capture.Inquiry.ToString(),
            capture.WireCommand,
            RawHex = FormatHex(capture.RawPayload),
            DeescapedHex = FormatHex(capture.DeescapedPayload),
            AsciiPreview = FormatAsciiPreview(capture.RawPayload),
            TokenHexBreakdown = FormatTokenHexBreakdown(capture.Tokens),
            capture.FrequencyHz,
            capture.StatusByte2Ratings,
            SafeCaptureNotice = SafeCaptureGuidance
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string FormatHex(byte[] bytes)
    {
        return BitConverter.ToString(bytes).Replace('-', ' ');
    }

    private static string FormatAsciiPreview(byte[] bytes)
    {
        char[] chars = new char[bytes.Length];
        for (int index = 0; index < bytes.Length; index++)
        {
            char candidate = (char)bytes[index];
            chars[index] = candidate >= PrintableAsciiMin && candidate <= PrintableAsciiMax
                ? candidate
                : '.';
        }

        return new string(chars);
    }

    private static IReadOnlyList<string> FormatTokenHexBreakdown(IReadOnlyList<byte[]> tokens)
    {
        string[] breakdown = new string[tokens.Count];
        for (int index = 0; index < tokens.Count; index++)
        {
            breakdown[index] = $"T{index}: {FormatHex(tokens[index])}";
        }

        return breakdown;
    }
}
