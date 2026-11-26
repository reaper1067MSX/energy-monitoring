namespace VoltTray.Hosting;

/// <summary>
/// Strategy contract for executing a configured VoltTray startup mode.
/// </summary>
public interface IApplicationModeStrategy
{
    int Execute();
}
