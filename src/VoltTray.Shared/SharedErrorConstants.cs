namespace VoltTray.Shared;

public static class SharedErrorConstants
{
    public const string DeviceNotFoundCode = "ups.device_not_found";
    public const string PortBusyContentionCode = "ups.port_busy_contention";
    public const string TransportTimeoutCode = "ups.transport_timeout";
    public const string ProtocolNakCode = "ups.protocol_nak";
    public const string MalformedFrameCode = "ups.malformed_frame";
    public const string QuarantinedAnomalyCode = "ups.quarantined_anomaly";
    public const string ValidationCode = "ups.validation";
    public const string UnexpectedSystemCode = "system.unexpected_exception";
    public const string DiagnosticDisableViewPowerScript = "disable-viewpower.ps1";
}
