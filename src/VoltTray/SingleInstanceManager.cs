using VoltTray.Hosting;

namespace VoltTray;

/// <summary>
/// Enforces single-instance execution across user sessions via a named Windows mutex.
/// </summary>
public static class SingleInstanceManager
{
    public static Mutex? TryAcquire(string name = HostConstants.DefaultMutexName)
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
            string localName = name.StartsWith(HostConstants.GlobalMutexPrefix, StringComparison.OrdinalIgnoreCase)
                ? name[HostConstants.GlobalMutexPrefix.Length..]
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
