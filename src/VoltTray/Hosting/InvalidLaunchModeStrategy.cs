using VoltTray.Shared;

namespace VoltTray.Hosting;

/// <summary>
/// Reports startup argument validation failures cleanly and exits with a non-zero code.
/// </summary>
public sealed class InvalidLaunchModeStrategy : IApplicationModeStrategy
{
    private readonly InvalidLaunchOptions _options;
    private readonly TextWriter _output;

    public InvalidLaunchModeStrategy(InvalidLaunchOptions options, TextWriter? output = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _output = output ?? Console.Out;
    }

    public int Execute()
    {
        _output.WriteLine(_options.ErrorMessage);
        return ApplicationExitCodes.Failure;
    }
}
