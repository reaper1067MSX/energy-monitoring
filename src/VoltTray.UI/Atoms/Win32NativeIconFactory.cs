using VoltTray.UI.Interop;

namespace VoltTray.UI.Atoms;

/// <summary>
/// Converts 32-bpp ARGB pixel buffers into Win32 HICON handles via CreateDIBSection.
/// </summary>
public sealed class Win32NativeIconFactory : INativeIconFactory
{
    public unsafe IntPtr CreateFromArgbPixels(uint[] pixels, int size)
    {
        ArgumentNullException.ThrowIfNull(pixels);

        var header = new BitmapInfoHeader
        {
            Size = (uint)sizeof(BitmapInfoHeader),
            Width = size,
            Height = -size,
            Planes = Win32UiConstants.ColorPlanesSingle,
            BitCount = Win32UiConstants.BitsPerPixel32,
            Compression = Win32UiConstants.BiRgb
        };

        IntPtr hbmColor = GdiNative.CreateDIBSection(
            IntPtr.Zero,
            ref header,
            Win32UiConstants.DibRgbColors,
            out IntPtr bitsPtr,
            IntPtr.Zero,
            0);
        if (hbmColor == IntPtr.Zero || bitsPtr == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        fixed (uint* srcPtr = pixels)
        {
            Buffer.MemoryCopy(
                srcPtr,
                (void*)bitsPtr,
                (long)pixels.Length * sizeof(uint),
                (long)pixels.Length * sizeof(uint));
        }

        IntPtr hbmMask = GdiNative.CreateBitmap(
            size,
            size,
            Win32UiConstants.ColorPlanesSingle,
            Win32UiConstants.ColorPlanesSingle,
            IntPtr.Zero);

        var info = new IconInfo
        {
            IsIcon = 1,
            ColorBitmap = hbmColor,
            MaskBitmap = hbmMask
        };

        IntPtr hIcon = GdiNative.CreateIconIndirect(ref info);
        GdiNative.DeleteObject(hbmColor);
        if (hbmMask != IntPtr.Zero)
        {
            GdiNative.DeleteObject(hbmMask);
        }

        return hIcon;
    }

    public void DestroyIcon(IntPtr iconHandle)
    {
        if (iconHandle != IntPtr.Zero)
        {
            GdiNative.DestroyIcon(iconHandle);
        }
    }
}
