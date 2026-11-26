using System.Globalization;
using System.Text.Json;
using VoltTray.Infrastructure.Hid;

namespace VoltTray.Cli;

/// <summary>
/// Formats HID descriptors, forensic capture payloads, and JSON test vectors for <c>--inspect</c>.
/// </summary>
public static class UpsProbeCaptureFormatter
{
    private const char PrintableAsciiMin = ' ';
    private const char PrintableAsciiMax = '~';

    public const string SafeCaptureGuidance =
        "Safe capture: wall-powered PC, uninterrupted PE ground, upstream Line (L) cut only, 25-60W resistive load, <=10s outage window.";

    public static void WriteDeviceDescriptors(IReadOnlyList<HidDeviceDescriptor> devices, TextWriter output)
    {
        foreach (HidDeviceDescriptor descriptor in devices)
        {
            string serial = string.IsNullOrEmpty(descriptor.SerialNumber) ? "(none)" : descriptor.SerialNumber;
            string line = string.Create(
                CultureInfo.InvariantCulture,
                $"VID_{descriptor.VendorId:X4}&PID_{descriptor.ProductId:X4} v{descriptor.VersionNumber:X4} | " +
                $"{descriptor.Manufacturer} {descriptor.Product} | Serial: {serial} | " +
                $"In:{descriptor.InputReportByteLength} Out:{descriptor.OutputReportByteLength}");

            output.WriteLine(line);
        }
    }

    public static void WriteCaptureDetails(UpsProbeCapture capture, TextWriter output)
    {
        for (int index = 0; index < capture.RawReports.Count; index++)
        {
            string reportLine = string.Create(
                CultureInfo.InvariantCulture,
                $"R#{index}: {FormatHex(capture.RawReports[index])}");
            output.WriteLine(reportLine);
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
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Frequency: {frequencyHz:F2} Hz"));
        }

        if (capture.StatusByte2Ratings is { } ratings)
        {
            string nominalAcText = ratings.NominalOutputVoltageAc.HasValue
                ? ratings.NominalOutputVoltageAc.Value.ToString(CultureInfo.InvariantCulture)
                : "Unknown";

            string statusLine = string.Create(
                CultureInfo.InvariantCulture,
                $"Status2: {ratings.NominalFrequencyHz} Hz, {ratings.NominalBatteryVoltageDc} V " +
                $"({ratings.BatteryBankSeriesCount}x12V), {nominalAcText} V");
            output.WriteLine(statusLine);
        }
    }

    public static string SerializeJson(UpsProbeCapture capture)
    {
        var document = new UpsProbeCaptureDocument(
            capture.Device,
            capture.Inquiry.ToString(),
            capture.WireCommand,
            FormatHex(capture.RawPayload),
            FormatHex(capture.DeescapedPayload),
            FormatAsciiPreview(capture.RawPayload),
            FormatTokenHexBreakdown(capture.Tokens),
            capture.FrequencyHz,
            capture.StatusByte2Ratings,
            SafeCaptureGuidance);

        return JsonSerializer.Serialize(document, UpsProbeJsonContext.Default.UpsProbeCaptureDocument);
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
            breakdown[index] = string.Create(
                CultureInfo.InvariantCulture,
                $"T{index}: {FormatHex(tokens[index])}");
        }

        return breakdown;
    }
}
