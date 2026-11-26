namespace VoltTray.Infrastructure.Protocol;

/// <summary>
/// Enforces the strict read-only inquiry allowlist for diagnostic USB HID UPS probing.
/// </summary>
public static class UpsInquiryCommandPolicy
{
    public static readonly IReadOnlySet<string> AllowedCommands = new HashSet<string>(StringComparer.Ordinal)
    {
        "M\r",
        "QS\r",
        "Q1\r",
        "QPI\r",
        "QID\r",
        "QRI\r",
        "QMD\r",
        "QVFW\r",
        "QBV\r",
        "QGS\r",
        "QMOD\r",
        "QFLAG\r",
        "F\r",
        "I\r"
    };

    /// <summary>
    /// Returns true when the command is explicitly permitted for read-only HID inquiry.
    /// </summary>
    public static bool IsReadOnlyInquiry(string? command)
    {
        return !string.IsNullOrEmpty(command) && AllowedCommands.Contains(command);
    }

    /// <summary>
    /// Validates that the command is in the read-only inquiry allowlist before any HID write occurs.
    /// </summary>
    public static string EnsureReadOnlyInquiry(string? command)
    {
        if (IsReadOnlyInquiry(command))
        {
            return command!;
        }

        string formattedCommand = command?.Replace("\r", "\\r") ?? string.Empty;
        throw new InvalidOperationException(
            $"Command '{formattedCommand}' is blocked by the read-only HID inquiry allowlist.");
    }
}
