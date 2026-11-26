using System.Runtime.InteropServices;

namespace VoltTray.Hosting.Interop;

/// <summary>
/// Win32 user32.dll P/Invoke declarations and message struct for the Win32 tray message loop.
/// </summary>
internal static class User32MessageNativeMethods
{
    public const int GetMessageErrorResult = -1;

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeMessage
    {
        public IntPtr Hwnd;
        public uint Message;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PointX;
        public int PointY;
        public uint Private;
    }

    [DllImport("user32.dll", EntryPoint = "GetMessageW")]
    internal static extern int GetMessage(
        out NativeMessage message,
        IntPtr windowHandle,
        uint filterMin,
        uint filterMax);

    [DllImport("user32.dll", EntryPoint = "TranslateMessage")]
    internal static extern bool TranslateMessage(ref NativeMessage message);

    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
    internal static extern IntPtr DispatchMessage(ref NativeMessage message);

    [DllImport("user32.dll", EntryPoint = "PostQuitMessage")]
    internal static extern void PostQuitMessage(int exitCode);
}
