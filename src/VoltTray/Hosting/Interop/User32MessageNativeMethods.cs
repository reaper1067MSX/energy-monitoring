using System.Runtime.InteropServices;

namespace VoltTray.Hosting.Interop;

/// <summary>
/// Source-generated Win32 user32.dll P/Invoke declarations for the tray message loop.
/// </summary>
internal static partial class User32MessageNativeMethods
{
    public const int GetMessageErrorResult = -1;

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
    internal static partial int GetMessage(
        out NativeMessage message,
        IntPtr windowHandle,
        uint filterMin,
        uint filterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TranslateMessage(ref NativeMessage message);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    internal static partial IntPtr DispatchMessage(ref NativeMessage message);

    [LibraryImport("user32.dll")]
    internal static partial void PostQuitMessage(int exitCode);
}
