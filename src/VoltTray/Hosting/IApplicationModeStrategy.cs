namespace VoltTray.Hosting;

/// <summary>
/// Strategy contract for executing a configured VoltTray startup mode.
/// </summary>
public interface IApplicationModeStrategy
{
    const int SuccessExitCode = 0;
    const int FailureExitCode = 1;

    int Execute();
}
