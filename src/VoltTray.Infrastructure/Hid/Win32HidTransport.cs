using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using VoltTray.Shared;

namespace VoltTray.Infrastructure.Hid;

/// <summary>
/// Opens Win32 Overlapped HID connections via <see cref="HidOverlappedIo"/>
/// and classifies sharing/access contention versus missing devices.
/// </summary>
internal sealed class Win32HidTransport : IHidTransport
{
    public Result<IHidConnection> Open(string devicePath)
    {
        SafeFileHandle handle = HidOverlappedIo.OpenDeviceHandle(devicePath);
        if (!handle.IsInvalid)
        {
            return Result<IHidConnection>.Success(new Win32HidConnection(handle));
        }

        int win32Error = Marshal.GetLastPInvokeError();
        handle.Dispose();
        return Result<IHidConnection>.Failure(MapOpenFailure(win32Error, devicePath));
    }

    internal static DomainError MapOpenFailure(int win32Error, string devicePath)
    {
        return win32Error switch
        {
            HidConstants.Win32ErrorSharingViolation or HidConstants.Win32ErrorBusy => new DomainError(
                SharedErrorConstants.PortBusyContentionCode,
                $"USB HID port is busy or locked ({SharedErrorConstants.DiagnosticDisableViewPowerScript}).",
                ErrorCategory.PortBusyContention,
                IsTransient: true),
            HidConstants.Win32ErrorAccessDenied => new DomainError(
                SharedErrorConstants.PortBusyContentionCode,
                $"Access denied opening USB HID device '{devicePath}' ({SharedErrorConstants.DiagnosticDisableViewPowerScript}).",
                ErrorCategory.PortBusyContention,
                IsTransient: false),
            _ => new DomainError(
                SharedErrorConstants.DeviceNotFoundCode,
                $"USB HID device '{devicePath}' was not found (Win32 error {win32Error}).",
                ErrorCategory.DeviceNotFound,
                IsTransient: false)
        };
    }
}
