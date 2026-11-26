namespace VoltTray.Hosting;

/// <summary>
/// Reports startup argument validation failures cleanly and exits with a non-zero code.
/// </summary>
public sealed class InvalidLaunchModeStrategy : IApplicationModeStrategy
{
    private const int FailureExitCode = 1;

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
        return FailureExitCode;
    }
}
