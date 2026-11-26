using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using VoltTray.Infrastructure.Hid.Interop;

namespace VoltTray.Infrastructure.Hid;

/// <summary>
/// Dynamically enumerates attached USB HID device interfaces via SetupAPI and hid.dll.
/// </summary>
public sealed class HidDeviceScanner : IHidDeviceScanner
{
    /// <summary>
    /// Discovers attached HID devices, defaulting to the Voltronic/Forza VID_0665&amp;PID_5161 target.
    /// </summary>
    public IReadOnlyList<HidDeviceDescriptor> DiscoverDevices(bool includeAllHidDevices = false)
    {
        List<HidDeviceDescriptor> descriptors = new();
        HidNativeMethods.HidD_GetHidGuid(out Guid hidGuid);

        IntPtr deviceInfoSet = HidNativeMethods.SetupDiGetClassDevsW(
            ref hidGuid,
            IntPtr.Zero,
            IntPtr.Zero,
            HidNativeMethods.DigcfPresent | HidNativeMethods.DigcfDeviceInterface);

        if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(HidNativeMethods.InvalidHandleValue))
        {
            return descriptors;
        }

        try
        {
            foreach (string devicePath in EnumerateInterfacePaths(deviceInfoSet, hidGuid))
            {
                HidDeviceDescriptor? descriptor = TryReadInterfaceDescriptor(devicePath);
                if (descriptor == null)
                {
                    continue;
                }

                bool matchesTargetUps =
                    descriptor.IsSupportedVoltronicDevice;

                if (includeAllHidDevices || matchesTargetUps)
                {
                    descriptors.Add(descriptor);
                }
            }
        }
        finally
        {
            HidNativeMethods.SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        return descriptors;
    }

    private static IReadOnlyList<string> EnumerateInterfacePaths(IntPtr deviceInfoSet, Guid hidGuid)
    {
        List<string> devicePaths = new();
        var interfaceData = new DeviceInterfaceData
        {
            Size = Marshal.SizeOf<DeviceInterfaceData>()
        };

        for (uint memberIndex = 0;
             HidNativeMethods.SetupDiEnumDeviceInterfaces(
                 deviceInfoSet,
                 IntPtr.Zero,
                 ref hidGuid,
                 memberIndex,
                 ref interfaceData);
             memberIndex++)
        {
            string? devicePath = TryGetInterfaceDevicePath(deviceInfoSet, ref interfaceData);
            if (!string.IsNullOrEmpty(devicePath))
            {
                devicePaths.Add(devicePath);
            }
        }

        return devicePaths;
    }

    private static string? TryGetInterfaceDevicePath(
        IntPtr deviceInfoSet,
        ref DeviceInterfaceData interfaceData)
    {
        HidNativeMethods.SetupDiGetDeviceInterfaceDetailW(
            deviceInfoSet,
            ref interfaceData,
            IntPtr.Zero,
            0,
            out int requiredSize,
            IntPtr.Zero);

        if (requiredSize <= HidNativeMethods.DevicePathOffsetBytes)
        {
            return null;
        }

        IntPtr detailBuffer = Marshal.AllocHGlobal(requiredSize);
        try
        {
            int headerSize = IntPtr.Size == 8
                ? HidNativeMethods.DeviceInterfaceDetailHeaderSize64
                : HidNativeMethods.DeviceInterfaceDetailHeaderSize32;

            Marshal.WriteInt32(detailBuffer, headerSize);

            bool succeeded = HidNativeMethods.SetupDiGetDeviceInterfaceDetailW(
                deviceInfoSet,
                ref interfaceData,
                detailBuffer,
                requiredSize,
                out _,
                IntPtr.Zero);

            if (!succeeded)
            {
                return null;
            }

            return Marshal.PtrToStringUni(IntPtr.Add(detailBuffer, HidNativeMethods.DevicePathOffsetBytes));
        }
        finally
        {
            Marshal.FreeHGlobal(detailBuffer);
        }
    }

    private static HidDeviceDescriptor? TryReadInterfaceDescriptor(string devicePath)
    {
        using SafeFileHandle handle = HidOverlappedIo.OpenDeviceHandle(
            devicePath,
            desiredAccess: 0,
            flagsAndAttributes: 0);

        if (handle.IsInvalid)
        {
            return null;
        }

        if (!ReadDescriptorAttributes(handle, out HidDeviceAttributes attributes))
        {
            return null;
        }

        ReadReportCapabilities(
            handle,
            out ushort inputReportByteLength,
            out ushort outputReportByteLength);

        string manufacturer = ReadHidString(handle, HidNativeMethods.HidD_GetManufacturerString);
        string product = ReadHidString(handle, HidNativeMethods.HidD_GetProductString);
        string serialNumber = ReadHidString(handle, HidNativeMethods.HidD_GetSerialNumberString);

        return new HidDeviceDescriptor(
            devicePath,
            attributes.VendorId,
            attributes.ProductId,
            attributes.VersionNumber,
            manufacturer,
            product,
            serialNumber,
            inputReportByteLength,
            outputReportByteLength);
    }

    private static bool ReadDescriptorAttributes(
        SafeFileHandle handle,
        out HidDeviceAttributes attributes)
    {
        attributes = new HidDeviceAttributes
        {
            Size = Marshal.SizeOf<HidDeviceAttributes>()
        };

        return HidNativeMethods.HidD_GetAttributes(handle, ref attributes);
    }

    private static void ReadReportCapabilities(
        SafeFileHandle handle,
        out ushort inputReportByteLength,
        out ushort outputReportByteLength)
    {
        inputReportByteLength = 0;
        outputReportByteLength = 0;

        if (!HidNativeMethods.HidD_GetPreparsedData(handle, out IntPtr preparsedData))
        {
            return;
        }

        try
        {
            uint status = HidNativeMethods.HidP_GetCaps(preparsedData, out HidCapabilities capabilities);
            if (status == HidNativeMethods.HidpStatusSuccess)
            {
                inputReportByteLength = capabilities.InputReportByteLength;
                outputReportByteLength = capabilities.OutputReportByteLength;
            }
        }
        finally
        {
            HidNativeMethods.HidD_FreePreparsedData(preparsedData);
        }
    }

    private static string ReadHidString(
        SafeFileHandle handle,
        Func<SafeFileHandle, IntPtr, int, bool> reader)
    {
        IntPtr buffer = Marshal.AllocHGlobal(HidNativeMethods.MaxHidStringBufferBytes);
        try
        {
            Marshal.WriteInt16(buffer, 0);
            if (!reader(handle, buffer, HidNativeMethods.MaxHidStringBufferBytes))
            {
                return string.Empty;
            }

            return Marshal.PtrToStringUni(buffer)?.Trim() ?? string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
