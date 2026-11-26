using VoltTray.Core.Models;
using VoltTray.Infrastructure.Protocol;

namespace VoltTray.Cli;

/// <summary>
/// Resolves CLI inquiry strings into strictly read-only <see cref="UpsDiagnosticInquiry"/> domain values.
/// </summary>
public static class InspectInquiryArgumentParser
{
    public static bool TryParse(string? rawCommand, out UpsDiagnosticInquiry inquiry)
    {
        return VoltronicInquiryCodec.TryParseWireCommand(rawCommand, out inquiry);
    }
}
