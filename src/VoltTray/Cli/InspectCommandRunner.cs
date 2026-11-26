using System.Text;
using System.Text.Json;
using VoltTray.Infrastructure.Hid;
using VoltTray.Infrastructure.Protocol;

namespace VoltTray.Cli;

/// <summary>
/// Executes the read-only <c>--inspect</c> diagnostic CLI flow and exports JSON test vectors.
/// </summary>
public static class InspectCommandRunner
{
    private const char PrintableAsciiMin = ' ';
    private const char PrintableAsciiMax = '~';

    public const string SafeCaptureGuidance =
        "Safe capture (--stream): wall-powered PC, uninterrupted PE, upstream Line (L) cut, 25-60W resistive load, <=10s outage window.";

    public static int Run(
        string[] args,
        TextWriter output,
        ReadOnlyUpsProbe? probe = null,
        HidDeviceScanner? scanner = null)
    {
        if (!TryParseArguments(args, out bool includeAllHidDevices, out string command, out string? exportPath, out string? errorMessage))
        {
            output.WriteLine(errorMessage);
            return 1;
        }

        try
        {
            UpsInquiryCommandPolicy.EnsureReadOnlyInquiry(command);
        }
        catch (InvalidOperationException ex)
        {
            output.WriteLine(ex.Message);
            return 1;
        }

        output.WriteLine(SafeCaptureGuidance);

        var activeScanner = scanner ?? new HidDeviceScanner();
        IReadOnlyList<HidDeviceDescriptor> devices = activeScanner.DiscoverDevices(includeAllHidDevices);
        WriteDeviceDescriptors(devices, output);

        var activeProbe = probe ?? new ReadOnlyUpsProbe(activeScanner);
        UpsProbeCapture? capture = activeProbe.InspectFirstMatchingDevice(command, includeAllHidDevices);
        if (capture == null)
        {
            output.WriteLine("No matching UPS HID device or response.");
            return 1;
        }

        WriteCaptureDetails(capture, output);

        if (!string.IsNullOrEmpty(exportPath))
        {
            File.WriteAllText(exportPath, SerializeCaptureJson(capture), Encoding.UTF8);
        }

        return 0;
    }

    public static string SerializeCaptureJson(UpsProbeCapture capture)
    {
        var payload = new
        {
            capture.Device,
            capture.Command,
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

    private static bool TryParseArguments(
        string[] args,
        out bool includeAllHidDevices,
        out string command,
        out string? exportPath,
        out string? errorMessage)
    {
        includeAllHidDevices = args.Any(arg => arg.Equals("--all-hid", StringComparison.OrdinalIgnoreCase));
        command = VoltronicProtocolConstants.QueryStatusCommand;
        exportPath = null;
        errorMessage = null;

        for (int index = 0; index < args.Length; index++)
        {
            if (args[index].Equals("--export", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    errorMessage = "Missing value for --export.";
                    return false;
                }

                exportPath = args[++index];
                continue;
            }

            if (args[index].Equals("--cmd", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    errorMessage = "Missing value for --cmd.";
                    return false;
                }

                string rawCommand = args[++index];
                command = rawCommand.EndsWith(VoltronicProtocolConstants.CarriageReturnChar)
                    ? rawCommand
                    : rawCommand + VoltronicProtocolConstants.CarriageReturnChar;
            }
        }

        return true;
    }

    private static void WriteDeviceDescriptors(IReadOnlyList<HidDeviceDescriptor> devices, TextWriter output)
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

    private static void WriteCaptureDetails(UpsProbeCapture capture, TextWriter output)
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
