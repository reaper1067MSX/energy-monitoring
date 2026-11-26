using System.Text;
using VoltTray.Core.Models;
using VoltTray.Hosting;
using VoltTray.Infrastructure.Hid;
using VoltTray.Infrastructure.Protocol;

namespace VoltTray.Cli;

/// <summary>
/// Executes the read-only <c>--inspect</c> diagnostic CLI mode.
/// </summary>
public sealed class InspectModeStrategy : IApplicationModeStrategy
{
    private readonly TextWriter _output;
    private readonly IHidDeviceScanner _scanner;
    private readonly ReadOnlyUpsProbe _probe;

    public InspectModeStrategy(
        TextWriter? output = null,
        ReadOnlyUpsProbe? probe = null,
        IHidDeviceScanner? scanner = null)
    {
        _output = output ?? Console.Out;
        _scanner = scanner ?? new HidDeviceScanner();
        _probe = probe ?? new ReadOnlyUpsProbe(_scanner);
    }

    public int Execute(ApplicationOptions options)
    {
        if (options.HasInspectCommandError)
        {
            _output.WriteLine(options.ErrorMessage);
            return 1;
        }

        if (!VoltronicInquiryCodec.TryParseWireCommand(options.RawInspectCommand, out UpsDiagnosticInquiry inquiry))
        {
            string formattedCommand = options.RawInspectCommand?.Replace("\r", "\\r") ?? string.Empty;
            _output.WriteLine($"Command '{formattedCommand}' is blocked by the read-only diagnostic inquiry policy.");
            return 1;
        }

        _output.WriteLine(UpsProbeCaptureFormatter.SafeCaptureGuidance);

        IReadOnlyList<HidDeviceDescriptor> devices = _scanner.DiscoverDevices(options.IncludeAllHidDevices);
        UpsProbeCaptureFormatter.WriteDeviceDescriptors(devices, _output);

        UpsProbeCapture? capture = _probe.InspectFirstMatchingDevice(inquiry, options.IncludeAllHidDevices);
        if (capture == null)
        {
            _output.WriteLine("No matching UPS HID device or response.");
            return 1;
        }

        UpsProbeCaptureFormatter.WriteCaptureDetails(capture, _output);

        if (!string.IsNullOrEmpty(options.ExportPath))
        {
            File.WriteAllText(options.ExportPath, UpsProbeCaptureFormatter.SerializeJson(capture), Encoding.UTF8);
        }

        return 0;
    }
}
