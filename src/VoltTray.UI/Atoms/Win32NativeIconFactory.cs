using Windows.Win32;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;
using VoltTray.UI.Interop;

namespace VoltTray.UI.Atoms;

/// <summary>
/// Converts 32-bpp ARGB pixel buffers into Win32 HICON handles via CreateDIBSection.
/// </summary>
internal sealed class Win32NativeIconFactory : INativeIconFactory
{
    public unsafe IntPtr CreateFromArgbPixels(uint[] pixels, int size)
    {
        ArgumentNullException.ThrowIfNull(pixels);

        var bmi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER),
                biWidth = size,
                biHeight = -size,
                biPlanes = Win32UiConstants.ColorPlanesSingle,
                biBitCount = Win32UiConstants.BitsPerPixel32,
                biCompression = (uint)BI_COMPRESSION.BI_RGB
            }
        };

        HBITMAP hbmColor = PInvoke.CreateDIBSection(
            default,
            &bmi,
            DIB_USAGE.DIB_RGB_COLORS,
            out void* bitsPtr,
            default,
            0);
        if (hbmColor.IsNull || bitsPtr == null)
        {
            return IntPtr.Zero;
        }

        fixed (uint* srcPtr = pixels)
        {
            Buffer.MemoryCopy(
                srcPtr,
                bitsPtr,
                (long)pixels.Length * sizeof(uint),
                (long)pixels.Length * sizeof(uint));
        }

        HBITMAP hbmMask = PInvoke.CreateBitmap(
            size,
            size,
            Win32UiConstants.ColorPlanesSingle,
            Win32UiConstants.ColorPlanesSingle,
            null);

        var info = new ICONINFO
        {
            fIcon = true,
            hbmColor = hbmColor,
            hbmMask = hbmMask
        };

        HICON hIcon = PInvoke.CreateIconIndirect(&info);
        PInvoke.DeleteObject(hbmColor);
        if (!hbmMask.IsNull)
        {
            PInvoke.DeleteObject(hbmMask);
        }

        return (IntPtr)hIcon.Value;
    }

    public void DestroyIcon(IntPtr iconHandle)
    {
        if (iconHandle != IntPtr.Zero)
        {
            PInvoke.DestroyIcon(new HICON(iconHandle));
        }
    }
}
