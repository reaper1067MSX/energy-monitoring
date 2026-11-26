using System.Runtime.InteropServices;
using VoltTray.Core.Ports;

namespace VoltTray.Infrastructure.OS;

/// <summary>
/// Win32 advapi32.dll implementation of ISafetyShutdownPort.
/// Supports dry-run simulation mode without terminating the host machine.
/// </summary>
public sealed class WindowsShutdownPort : ISafetyShutdownPort
{
    [DllImport("advapi32.dll", EntryPoint = "InitiateSystemShutdownExW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool InitiateSystemShutdownEx(
        string? lpMachineName,
        string? lpMessage,
        uint dwTimeout,
        bool bForceAppsClosed,
        bool bRebootAfterShutdown,
        uint dwReason);

    [DllImport("advapi32.dll", EntryPoint = "AbortSystemShutdownW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool AbortSystemShutdown(string? lpMachineName);

    public Action<string>? OnDryRunExecuted { get; set; }

    public bool InitiateShutdown(bool dryRun, string reason)
    {
        if (dryRun)
        {
            OnDryRunExecuted?.Invoke($"[DRY-RUN] Shutdown simulated: {reason}");
            return true;
        }

        uint reasonCode =
            Win32ShutdownConstants.MajorPower |
            Win32ShutdownConstants.MinorEnvironment |
            Win32ShutdownConstants.FlagPlanned;
        return InitiateSystemShutdownEx(
            null,
            reason,
            Win32ShutdownConstants.DefaultTimeoutSeconds,
            true,
            false,
            reasonCode);
    }

    public bool AbortShutdown()
    {
        return AbortSystemShutdown(null);
    }
}
