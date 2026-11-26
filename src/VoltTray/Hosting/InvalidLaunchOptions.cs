namespace VoltTray.Hosting;

/// <summary>
/// Represents rejected or malformed command-line arguments to be surfaced cleanly at startup.
/// </summary>
public sealed record InvalidLaunchOptions(string ErrorMessage) : ApplicationLaunchOptions;
