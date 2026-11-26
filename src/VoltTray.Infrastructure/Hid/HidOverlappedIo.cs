using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using VoltTray.Infrastructure.Hid.Interop;

namespace VoltTray.Infrastructure.Hid;

/// <summary>
/// Manages Win32 Overlapped HID handle opening, cancellation, draining, and timed report transfers.
/// </summary>
internal static class HidOverlappedIo
{
    public static SafeFileHandle OpenDeviceHandle(
        string devicePath,
        uint desiredAccess = HidNativeMethods.GenericRead | HidNativeMethods.GenericWrite,
        uint flagsAndAttributes = HidNativeMethods.FileFlagOverlapped)
    {
        return HidNativeMethods.CreateFile(
            devicePath,
            desiredAccess,
            HidNativeMethods.FileShareRead | HidNativeMethods.FileShareWrite,
            IntPtr.Zero,
            HidNativeMethods.OpenExisting,
            flagsAndAttributes,
            IntPtr.Zero);
    }

    public static void CancelPendingIo(SafeFileHandle handle)
    {
        HidNativeMethods.CancelIoEx(handle, IntPtr.Zero);
    }

    public static void DrainInputBuffer(SafeFileHandle handle, int drainTimeoutMs)
    {
        byte[] drainBuffer = new byte[HidConstants.ReportLength];
        while (ReadReport(handle, drainBuffer, drainTimeoutMs) > 0)
        {
        }
    }

    public static int ReadReport(SafeFileHandle handle, byte[] buffer, int timeoutMs)
    {
        IntPtr eventHandle = HidNativeMethods.CreateEvent(IntPtr.Zero, manualReset: true, initialState: false, name: null);
        IntPtr overlappedPointer = Marshal.AllocHGlobal(Marshal.SizeOf<HidNativeMethods.NativeOverlappedIo>());
        IntPtr nativeBuffer = Marshal.AllocHGlobal(buffer.Length);
        Marshal.StructureToPtr(new HidNativeMethods.NativeOverlappedIo { EventHandle = eventHandle }, overlappedPointer, fDeleteOld: false);

        try
        {
            bool completedSynchronously = HidNativeMethods.ReadFile(
                handle,
                nativeBuffer,
                (uint)buffer.Length,
                out uint bytesRead,
                overlappedPointer);

            if (!completedSynchronously &&
                !WaitForOverlappedCompletion(handle, eventHandle, overlappedPointer, timeoutMs, out bytesRead))
            {
                return 0;
            }

            int byteCount = (int)Math.Min(bytesRead, (uint)buffer.Length);
            if (byteCount > 0)
            {
                Marshal.Copy(nativeBuffer, buffer, 0, byteCount);
            }

            return byteCount;
        }
        finally
        {
            HidNativeMethods.CloseHandle(eventHandle);
            Marshal.FreeHGlobal(overlappedPointer);
            Marshal.FreeHGlobal(nativeBuffer);
        }
    }

    public static bool WriteReport(SafeFileHandle handle, byte[] buffer, int timeoutMs)
    {
        IntPtr eventHandle = HidNativeMethods.CreateEvent(IntPtr.Zero, manualReset: true, initialState: false, name: null);
        IntPtr overlappedPointer = Marshal.AllocHGlobal(Marshal.SizeOf<HidNativeMethods.NativeOverlappedIo>());
        IntPtr nativeBuffer = Marshal.AllocHGlobal(buffer.Length);
        Marshal.StructureToPtr(new HidNativeMethods.NativeOverlappedIo { EventHandle = eventHandle }, overlappedPointer, fDeleteOld: false);
        Marshal.Copy(buffer, 0, nativeBuffer, buffer.Length);

        try
        {
            bool completedSynchronously = HidNativeMethods.WriteFile(
                handle,
                nativeBuffer,
                (uint)buffer.Length,
                out uint bytesWritten,
                overlappedPointer);

            if (!completedSynchronously &&
                !WaitForOverlappedCompletion(handle, eventHandle, overlappedPointer, timeoutMs, out bytesWritten))
            {
                return false;
            }

            return bytesWritten == buffer.Length;
        }
        finally
        {
            HidNativeMethods.CloseHandle(eventHandle);
            Marshal.FreeHGlobal(overlappedPointer);
            Marshal.FreeHGlobal(nativeBuffer);
        }
    }

    private static bool WaitForOverlappedCompletion(
        SafeFileHandle handle,
        IntPtr eventHandle,
        IntPtr overlappedPointer,
        int timeoutMs,
        out uint bytesTransferred)
    {
        bytesTransferred = 0;
        if (Marshal.GetLastWin32Error() != HidNativeMethods.ErrorIoPending)
        {
            return false;
        }

        if (HidNativeMethods.WaitForSingleObject(eventHandle, (uint)timeoutMs) == HidNativeMethods.WaitObject0)
        {
            return HidNativeMethods.GetOverlappedResult(handle, overlappedPointer, out bytesTransferred, wait: false);
        }

        HidNativeMethods.CancelIoEx(handle, overlappedPointer);
        HidNativeMethods.GetOverlappedResult(handle, overlappedPointer, out _, wait: true);
        return false;
    }
}
