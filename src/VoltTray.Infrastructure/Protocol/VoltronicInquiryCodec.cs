using VoltTray.Core.Diagnostics;

namespace VoltTray.Infrastructure.Protocol;

/// <summary>
/// Maps between domain <see cref="UpsDiagnosticInquiry"/> operations and Voltronic/Megatec wire commands.
/// </summary>
public static class VoltronicInquiryCodec
{
    /// <summary>
    /// Converts a read-only domain diagnostic inquiry into its terminated wire command string.
    /// </summary>
    public static string ToWireCommand(UpsDiagnosticInquiry inquiry)
    {
        return inquiry switch
        {
            UpsDiagnosticInquiry.ProtocolIdentity => VoltronicProtocolConstants.ProtocolIdentityCommand,
            UpsDiagnosticInquiry.QuickStatus => VoltronicProtocolConstants.QueryStatusCommand,
            UpsDiagnosticInquiry.StandardAsciiStatus => VoltronicProtocolConstants.StandardAsciiStatusCommand,
            UpsDiagnosticInquiry.ProtocolId => VoltronicProtocolConstants.ProtocolIdCommand,
            UpsDiagnosticInquiry.DeviceId => VoltronicProtocolConstants.DeviceIdCommand,
            UpsDiagnosticInquiry.RatedInformation => VoltronicProtocolConstants.RatedInformationCommand,
            UpsDiagnosticInquiry.ModelDescriptor => VoltronicProtocolConstants.ModelDescriptorCommand,
            UpsDiagnosticInquiry.FirmwareVersion => VoltronicProtocolConstants.FirmwareVersionCommand,
            UpsDiagnosticInquiry.BatteryStatus => VoltronicProtocolConstants.BatteryStatusCommand,
            UpsDiagnosticInquiry.GeneralStatus => VoltronicProtocolConstants.GeneralStatusCommand,
            UpsDiagnosticInquiry.OperatingMode => VoltronicProtocolConstants.OperatingModeCommand,
            UpsDiagnosticInquiry.FeatureFlags => VoltronicProtocolConstants.FeatureFlagsCommand,
            UpsDiagnosticInquiry.NominalRatings => VoltronicProtocolConstants.NominalRatingsCommand,
            UpsDiagnosticInquiry.VendorInformation => VoltronicProtocolConstants.VendorInformationCommand,
            _ => throw new ArgumentOutOfRangeException(nameof(inquiry), inquiry, "Unsupported diagnostic inquiry.")
        };
    }

    /// <summary>
    /// Parses a wire command or CLI command token into a read-only <see cref="UpsDiagnosticInquiry"/>;
    /// returns false for mutating or unrecognized commands.
    /// </summary>
    public static bool TryParseWireCommand(string? rawCommand, out UpsDiagnosticInquiry inquiry)
    {
        if (string.IsNullOrEmpty(rawCommand))
        {
            inquiry = default;
            return false;
        }

        string normalizedCommand = rawCommand.EndsWith(VoltronicProtocolConstants.CarriageReturnChar)
            ? rawCommand
            : rawCommand + VoltronicProtocolConstants.CarriageReturnChar;

        switch (normalizedCommand)
        {
            case VoltronicProtocolConstants.ProtocolIdentityCommand:
                inquiry = UpsDiagnosticInquiry.ProtocolIdentity;
                return true;
            case VoltronicProtocolConstants.QueryStatusCommand:
                inquiry = UpsDiagnosticInquiry.QuickStatus;
                return true;
            case VoltronicProtocolConstants.StandardAsciiStatusCommand:
                inquiry = UpsDiagnosticInquiry.StandardAsciiStatus;
                return true;
            case VoltronicProtocolConstants.ProtocolIdCommand:
                inquiry = UpsDiagnosticInquiry.ProtocolId;
                return true;
            case VoltronicProtocolConstants.DeviceIdCommand:
                inquiry = UpsDiagnosticInquiry.DeviceId;
                return true;
            case VoltronicProtocolConstants.RatedInformationCommand:
                inquiry = UpsDiagnosticInquiry.RatedInformation;
                return true;
            case VoltronicProtocolConstants.ModelDescriptorCommand:
                inquiry = UpsDiagnosticInquiry.ModelDescriptor;
                return true;
            case VoltronicProtocolConstants.FirmwareVersionCommand:
                inquiry = UpsDiagnosticInquiry.FirmwareVersion;
                return true;
            case VoltronicProtocolConstants.BatteryStatusCommand:
                inquiry = UpsDiagnosticInquiry.BatteryStatus;
                return true;
            case VoltronicProtocolConstants.GeneralStatusCommand:
                inquiry = UpsDiagnosticInquiry.GeneralStatus;
                return true;
            case VoltronicProtocolConstants.OperatingModeCommand:
                inquiry = UpsDiagnosticInquiry.OperatingMode;
                return true;
            case VoltronicProtocolConstants.FeatureFlagsCommand:
                inquiry = UpsDiagnosticInquiry.FeatureFlags;
                return true;
            case VoltronicProtocolConstants.NominalRatingsCommand:
                inquiry = UpsDiagnosticInquiry.NominalRatings;
                return true;
            case VoltronicProtocolConstants.VendorInformationCommand:
                inquiry = UpsDiagnosticInquiry.VendorInformation;
                return true;
            default:
                inquiry = default;
                return false;
        }
    }
}
