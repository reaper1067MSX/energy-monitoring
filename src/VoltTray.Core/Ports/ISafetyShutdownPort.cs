namespace VoltTray.Core.Ports;

public interface ISafetyShutdownPort
{
    bool InitiateShutdown(bool dryRun, string reason);
    bool AbortShutdown();
}
