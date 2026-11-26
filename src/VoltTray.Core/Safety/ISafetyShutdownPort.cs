namespace VoltTray.Core.Safety;

public interface ISafetyShutdownPort
{
    bool InitiateShutdown(bool dryRun, string reason);
    bool AbortShutdown();
}
