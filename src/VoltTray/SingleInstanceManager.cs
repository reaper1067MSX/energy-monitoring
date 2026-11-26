namespace VoltTray;

/// <summary>
/// Enforces single-instance execution across user sessions via a named Windows mutex.
/// </summary>
public static class SingleInstanceManager
{
    public const string DefaultMutexName = @"Global\VoltTray_SingleInstance_Mutex";
    private const string GlobalPrefix = @"Global\";

    public static Mutex? TryAcquire(string name = DefaultMutexName)
    {
        try
        {
            var mutex = new Mutex(initiallyOwned: true, name, out bool createdNew);
            if (!createdNew)
            {
                mutex.Dispose();
                return null;
            }

            return mutex;
        }
        catch (UnauthorizedAccessException)
        {
            return TryAcquireLocalFallback(name);
        }
        catch
        {
            return null;
        }
    }

    private static Mutex? TryAcquireLocalFallback(string name)
    {
        try
        {
            string localName = name.StartsWith(GlobalPrefix, StringComparison.OrdinalIgnoreCase)
                ? name[GlobalPrefix.Length..]
                : name;

            var mutex = new Mutex(initiallyOwned: true, localName, out bool createdNew);
            if (!createdNew)
            {
                mutex.Dispose();
                return null;
            }

            return mutex;
        }
        catch
        {
            return null;
        }
    }
}
