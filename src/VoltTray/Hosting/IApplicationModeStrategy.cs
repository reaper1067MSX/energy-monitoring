namespace VoltTray.Hosting;

/// <summary>
/// Strategy contract for executing a top-level VoltTray application mode.
/// </summary>
public interface IApplicationModeStrategy
{
    int Execute(ApplicationOptions options);
}
