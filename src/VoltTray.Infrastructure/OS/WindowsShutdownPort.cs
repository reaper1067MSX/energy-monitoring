using System.Runtime.InteropServices;
using VoltTray.Core.Ports;

namespace VoltTray.Infrastructure.OS;

/// <summary>
/// Win32 advapi32.dll implementation of ISafetyShutdownPort.
/// Supports dry-run simulation mode without terminating the host machine.
/// </summary>
public sealed class WindowsShutdownPort : ISafetyShutdownPort
{
    private const uint SHTDN_REASON_MAJOR_POWER = 0x00060000;
    private const uint SHTDN_REASON_MINOR_ENVIRONMENT = 0x0000000c;
    private const uint SHTDN_REASON_FLAG_PLANNED = 0x80000000;

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

        uint reasonCode = SHTDN_REASON_MAJOR_POWER | SHTDN_REASON_MINOR_ENVIRONMENT | SHTDN_REASON_FLAG_PLANNED;
        return InitiateSystemShutdownEx(null, reason, 30, true, false, reasonCode);
    }

    public bool AbortShutdown()
    {
        return AbortSystemShutdown(null);
    }
}
