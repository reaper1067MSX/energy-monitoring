namespace VoltTray.Hosting;

/// <summary>
/// Discriminated union root representing validated command-line launch options for VoltTray.
/// </summary>
public abstract record ApplicationLaunchOptions
{
    private protected ApplicationLaunchOptions()
    {
    }
}
