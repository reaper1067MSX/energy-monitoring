namespace VoltTray.Shared;

public enum ErrorCategory
{
    DeviceNotFound = 0,
    PortBusyContention = 1,
    TransportTimeout = 2,
    ProtocolNak = 3,
    MalformedFrame = 4,
    QuarantinedAnomaly = 5,
    Validation = 6,
    UnexpectedSystem = 7
}
