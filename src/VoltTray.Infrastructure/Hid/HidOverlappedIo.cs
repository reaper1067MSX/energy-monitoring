using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using VoltTray.Infrastructure.Hid.Interop;

namespace VoltTray.Infrastructure.Hid;

/// <summary>
/// Manages Win32 Overlapped HID handle opening, cancellation, draining, and timed report transfers.
/// </summary>
internal static class HidOverlappedIo
{
    private const int MinimumTimeoutMs = 0;

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
        if (!handle.IsInvalid && !handle.IsClosed)
        {
            HidNativeMethods.CancelIoEx(handle, IntPtr.Zero);
        }
    }

    public static void DrainInputBuffer(SafeFileHandle handle, int drainTimeoutMs)
    {
        byte[] drainBuffer = new byte[HidConstants.ReportLength];
        while (ReadReport(handle, drainBuffer, drainTimeoutMs) > 0)
        {
        }
    }

    public static int ClampTimeoutMs(int timeoutMs)
    {
        return Math.Max(MinimumTimeoutMs, timeoutMs);
    }

    public static int ReadReport(SafeFileHandle handle, byte[] buffer, int timeoutMs)
    {
        if (handle.IsInvalid || handle.IsClosed)
        {
            return 0;
        }

        IntPtr eventHandle = IntPtr.Zero;
        IntPtr overlappedPointer = IntPtr.Zero;
        IntPtr nativeBuffer = IntPtr.Zero;

        try
        {
            eventHandle = HidNativeMethods.CreateEvent(IntPtr.Zero, manualReset: true, initialState: false, name: null);
            if (eventHandle == IntPtr.Zero)
            {
                return 0;
            }

            overlappedPointer = Marshal.AllocHGlobal(Marshal.SizeOf<HidNativeMethods.NativeOverlappedIo>());
            nativeBuffer = Marshal.AllocHGlobal(buffer.Length);
            Marshal.StructureToPtr(
                new HidNativeMethods.NativeOverlappedIo { EventHandle = eventHandle },
                overlappedPointer,
                fDeleteOld: false);

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
            ReleaseOverlappedResources(eventHandle, overlappedPointer, nativeBuffer);
        }
    }

    public static bool WriteReport(SafeFileHandle handle, byte[] buffer, int timeoutMs)
    {
        if (handle.IsInvalid || handle.IsClosed)
        {
            return false;
        }

        IntPtr eventHandle = IntPtr.Zero;
        IntPtr overlappedPointer = IntPtr.Zero;
        IntPtr nativeBuffer = IntPtr.Zero;

        try
        {
            eventHandle = HidNativeMethods.CreateEvent(IntPtr.Zero, manualReset: true, initialState: false, name: null);
            if (eventHandle == IntPtr.Zero)
            {
                return false;
            }

            overlappedPointer = Marshal.AllocHGlobal(Marshal.SizeOf<HidNativeMethods.NativeOverlappedIo>());
            nativeBuffer = Marshal.AllocHGlobal(buffer.Length);
            Marshal.StructureToPtr(
                new HidNativeMethods.NativeOverlappedIo { EventHandle = eventHandle },
                overlappedPointer,
                fDeleteOld: false);
            Marshal.Copy(buffer, 0, nativeBuffer, buffer.Length);

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
            ReleaseOverlappedResources(eventHandle, overlappedPointer, nativeBuffer);
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

        uint clampedTimeoutMs = (uint)ClampTimeoutMs(timeoutMs);
        if (HidNativeMethods.WaitForSingleObject(eventHandle, clampedTimeoutMs) == HidNativeMethods.WaitObject0)
        {
            return HidNativeMethods.GetOverlappedResult(handle, overlappedPointer, out bytesTransferred, wait: false);
        }

        HidNativeMethods.CancelIoEx(handle, overlappedPointer);
        HidNativeMethods.GetOverlappedResult(handle, overlappedPointer, out _, wait: true);
        return false;
    }

    private static void ReleaseOverlappedResources(
        IntPtr eventHandle,
        IntPtr overlappedPointer,
        IntPtr nativeBuffer)
    {
        if (overlappedPointer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(overlappedPointer);
        }

        if (nativeBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(nativeBuffer);
        }

        if (eventHandle != IntPtr.Zero)
        {
            HidNativeMethods.CloseHandle(eventHandle);
        }
    }
}
