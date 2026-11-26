using System.Text;
using VoltTray.Hosting;
using VoltTray.Infrastructure.Hid;

namespace VoltTray.Cli;

/// <summary>
/// Executes the read-only <c>--inspect</c> diagnostic CLI mode with a single SetupDi device scan.
/// </summary>
public sealed class InspectModeStrategy : IApplicationModeStrategy
{
    private readonly InspectLaunchOptions _options;
    private readonly IUpsDiagnosticProbe _probe;
    private readonly TextWriter _output;

    public InspectModeStrategy(
        InspectLaunchOptions options,
        IUpsDiagnosticProbe? probe = null,
        TextWriter? output = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _probe = probe ?? new ReadOnlyUpsProbe();
        _output = output ?? Console.Out;
    }

    public int Execute()
    {
        _output.WriteLine(UpsProbeCaptureFormatter.SafeCaptureGuidance);

        IReadOnlyList<HidDeviceDescriptor> devices = _probe.DiscoverDevices(_options.IncludeAllHidDevices);
        UpsProbeCaptureFormatter.WriteDeviceDescriptors(devices, _output);

        HidDeviceDescriptor? targetDevice = devices.FirstOrDefault(descriptor => descriptor.IsSupportedVoltronicDevice);
        if (targetDevice == null)
        {
            _output.WriteLine("No matching UPS HID device found.");
            return IApplicationModeStrategy.FailureExitCode;
        }

        UpsProbeCapture? capture = _probe.QueryDevice(targetDevice, _options.Inquiry);
        if (capture == null)
        {
            _output.WriteLine("No response received from UPS HID device.");
            return IApplicationModeStrategy.FailureExitCode;
        }

        UpsProbeCaptureFormatter.WriteCaptureDetails(capture, _output);

        if (!string.IsNullOrEmpty(_options.ExportPath))
        {
            File.WriteAllText(_options.ExportPath, UpsProbeCaptureFormatter.SerializeJson(capture), Encoding.UTF8);
        }

        return IApplicationModeStrategy.SuccessExitCode;
    }
}
