using VoltTray.Core.Telemetry;

namespace VoltTray.Infrastructure.Protocol;

public sealed record NegotiatedUpsProtocol(
    IUpsProtocolStrategy Strategy,
    UpsCapabilities Capabilities);
