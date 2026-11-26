using System.Runtime.InteropServices;
using VoltTray.Core.Safety;

namespace VoltTray.Infrastructure.OS;

/// <summary>
/// Win32 advapi32.dll implementation of ISafetyShutdownPort.
/// Supports dry-run simulation mode without terminating the host machine.
/// </summary>
public sealed partial class WindowsShutdownPort : ISafetyShutdownPort
{
    [LibraryImport("advapi32.dll", EntryPoint = "InitiateSystemShutdownExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InitiateSystemShutdownEx(
        string? lpMachineName,
        string? lpMessage,
        uint dwTimeout,
        [MarshalAs(UnmanagedType.Bool)] bool bForceAppsClosed,
        [MarshalAs(UnmanagedType.Bool)] bool bRebootAfterShutdown,
        uint dwReason);

    [LibraryImport("advapi32.dll", EntryPoint = "AbortSystemShutdownW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AbortSystemShutdown(string? lpMachineName);

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
