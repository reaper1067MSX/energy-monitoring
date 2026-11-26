namespace VoltTray.Core.Diagnostics;

/// <summary>
/// Represents the strictly read-only UPS diagnostic inquiry operations permitted by the domain.
/// </summary>
public enum UpsDiagnosticInquiry
{
    ProtocolIdentity,
    QuickStatus,
    StandardAsciiStatus,
    ProtocolId,
    DeviceId,
    RatedInformation,
    ModelDescriptor,
    FirmwareVersion,
    BatteryStatus,
    GeneralStatus,
    OperatingMode,
    FeatureFlags,
    NominalRatings,
    VendorInformation
}
