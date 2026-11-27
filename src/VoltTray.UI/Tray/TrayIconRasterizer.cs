using System.Runtime.InteropServices;
using SkiaSharp;
using VoltTray.Core.Telemetry;
using VoltTray.UI.Atoms;
using VoltTray.UI.Interop;
using VoltTray.UI.Tokens.Primitives;

namespace VoltTray.UI.Tray;

/// <summary>
/// Renders anti-aliased 32-bpp premultiplied ARGB tray icons using Fluent Style A
/// parameterized SVG geometry via Svg.Skia, adapting stroke/accent contrast to Light and Dark taskbars.
/// </summary>
internal sealed class TrayIconRasterizer : ITrayIconRasterizer, IDisposable
{
    private readonly ISvgIconLibrary _svgIconLibrary;
    private readonly bool _ownsSvgIconLibrary;
    private bool _disposed;

    public TrayIconRasterizer(ISvgIconLibrary? svgIconLibrary = null)
    {
        if (svgIconLibrary != null)
        {
            _svgIconLibrary = svgIconLibrary;
            _ownsSvgIconLibrary = false;
        }
        else
        {
            _svgIconLibrary = new SkiaSvgIconLibrary();
            _ownsSvgIconLibrary = true;
        }
    }

    public static TrayIconRasterizer CreateDefault() => new();

    public int ResolvePhysicalSize(int dpi)
    {
        if (dpi >= Win32UiConstants.Dpi200)
        {
            return LayoutTokens.TrayIconPx[3];
        }

        if (dpi >= Win32UiConstants.Dpi150)
        {
            return LayoutTokens.TrayIconPx[2];
        }

        if (dpi >= Win32UiConstants.Dpi125)
        {
            return LayoutTokens.TrayIconPx[1];
        }

        return LayoutTokens.TrayIconPx[0];
    }

    public uint[] RenderArgbPixels(UpsOperationalState state, int batteryPct, int size, bool isLightTaskbar)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int clampedSize = Math.Max(LayoutTokens.TrayIconPx[0], size);
        using SKSurface surface = RenderSurface(state, batteryPct, clampedSize, isLightTaskbar);
        var imageInfo = new SKImageInfo(clampedSize, clampedSize, SKColorType.Bgra8888, SKAlphaType.Premul);
        uint[] pixels = new uint[clampedSize * clampedSize];

        GCHandle handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            surface.ReadPixels(imageInfo, handle.AddrOfPinnedObject(), clampedSize * sizeof(uint), 0, 0);
        }
        finally
        {
            handle.Free();
        }

        return pixels;
    }

    public byte[] RenderPng(UpsOperationalState state, int batteryPct, int size, bool isLightTaskbar)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int clampedSize = Math.Max(LayoutTokens.TrayIconPx[0], size);
        using SKSurface surface = RenderSurface(state, batteryPct, clampedSize, isLightTaskbar);
        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, TrayIconRenderConstants.PngQuality);
        return data.ToArray();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsSvgIconLibrary)
        {
            _svgIconLibrary.Dispose();
        }
    }

    private SKSurface RenderSurface(UpsOperationalState state, int batteryPct, int size, bool isLightTaskbar)
    {
        var imageInfo = new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        SKSurface surface = SKSurface.Create(imageInfo)
            ?? throw new InvalidOperationException("Failed to allocate SkiaSharp tray icon surface.");

        SKCanvas canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        string cacheKey = RingBatterySvgBuilder.BuildCacheKey(state, batteryPct, isLightTaskbar);
        string svgMarkup = RingBatterySvgBuilder.BuildIconSvg(state, batteryPct, isLightTaskbar);
        _svgIconLibrary.TryDrawMarkup(canvas, cacheKey, svgMarkup, new SKRect(0, 0, size, size));

        return surface;
    }
}
