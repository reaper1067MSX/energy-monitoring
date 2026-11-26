using VoltTray.Core.Models;

namespace VoltTray.Infrastructure.Protocol;

public sealed record NegotiatedUpsProtocol(
    IUpsProtocolStrategy Strategy,
    UpsCapabilities Capabilities);
