namespace VoltTray.Core.Models;

/// <summary>
/// Represents the strictly read-only UPS diagnostic inquiry operations permitted by the domain.
/// </summary>
public enum UpsDiagnosticInquiry
{
    ProtocolIdentity,
    QuickStatus,
    MegatecStatus,
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
