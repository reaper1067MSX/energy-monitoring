using System.Runtime.InteropServices;

namespace VoltTray.UI.Interop;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NotifyIconDataW
{
    public uint Size;
    public IntPtr WindowHandle;
    public uint Id;
    public uint Flags;
    public uint CallbackMessage;
    public IntPtr IconHandle;
    public fixed char Tip[Win32UiConstants.NotifyTipCapacity];
    public uint State;
    public uint StateMask;
    public fixed char Info[Win32UiConstants.NotifyInfoCapacity];
    public uint TimeoutOrVersion;
    public fixed char InfoTitle[Win32UiConstants.NotifyInfoTitleCapacity];
    public uint InfoFlags;
    public Guid GuidItem;
    public IntPtr BalloonIconHandle;

    public void SetTip(string? text)
    {
        fixed (char* tipPtr = Tip)
        {
            var span = new Span<char>(tipPtr, Win32UiConstants.NotifyTipCapacity);
            span.Clear();

            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            int copyLength = Math.Min(text.Length, Win32UiConstants.NotifyTipCapacity - 1);
            text.AsSpan(0, copyLength).CopyTo(span);
        }
    }
}
