using System.Reflection;
using SkiaSharp;
using Svg.Skia;

namespace VoltTray.UI.Atoms;

/// <summary>
/// Loads embedded handoff SVG assets once, caches parsed SKSvg graphs,
/// and renders crisp anti-aliased premultiplied ARGB graphics via SkiaSharp.
/// </summary>
public sealed class SkiaSvgIconLibrary : ISvgIconLibrary
{
    private readonly Assembly _resourceAssembly = typeof(SkiaSvgIconLibrary).Assembly;
    private readonly Dictionary<string, SKSvg?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cacheLock = new();
    private bool _isDisposed;

    public bool TryDraw(SKCanvas canvas, string iconName, SKRect destination, SKColor? tintColor = null)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (string.IsNullOrWhiteSpace(iconName) || destination.Width <= 0 || destination.Height <= 0)
        {
            return false;
        }

        SKSvg? svg = GetOrLoad(iconName);
        if (svg?.Picture is not SKPicture picture)
        {
            return false;
        }

        SKRect cullRect = picture.CullRect;
        if (cullRect.Width <= 0 || cullRect.Height <= 0)
        {
            return false;
        }

        float scale = Math.Min(destination.Width / cullRect.Width, destination.Height / cullRect.Height);
        float offsetX = destination.Left + ((destination.Width - (cullRect.Width * scale)) / 2f);
        float offsetY = destination.Top + ((destination.Height - (cullRect.Height * scale)) / 2f);

        var matrix = SKMatrix.CreateScaleTranslation(scale, scale, offsetX, offsetY);

        if (tintColor is SKColor tint)
        {
            using var filter = SKColorFilter.CreateBlendMode(tint, SKBlendMode.SrcIn);
            using var paint = new SKPaint
            {
                IsAntialias = true,
                ColorFilter = filter
            };
            canvas.DrawPicture(picture, in matrix, paint);
        }
        else
        {
            using var paint = new SKPaint { IsAntialias = true };
            canvas.DrawPicture(picture, in matrix, paint);
        }

        return true;
    }

    public unsafe uint[]? RenderArgbPixels(string iconName, int size, SKColor? tintColor = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);

        var info = new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using SKSurface? surface = SKSurface.Create(info);
        if (surface == null)
        {
            return null;
        }

        surface.Canvas.Clear(SKColors.Transparent);
        if (!TryDraw(surface.Canvas, iconName, new SKRect(0, 0, size, size), tintColor))
        {
            return null;
        }

        surface.Canvas.Flush();
        uint[] pixels = new uint[size * size];
        fixed (uint* dstPtr = pixels)
        {
            if (!surface.ReadPixels(info, (IntPtr)dstPtr, size * sizeof(uint), 0, 0))
            {
                return null;
            }
        }

        return pixels;
    }

    public void Dispose()
    {
        lock (_cacheLock)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            foreach (SKSvg? svg in _cache.Values)
            {
                svg?.Dispose();
            }

            _cache.Clear();
        }
    }

    private SKSvg? GetOrLoad(string iconName)
    {
        lock (_cacheLock)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (_cache.TryGetValue(iconName, out SKSvg? cached))
            {
                return cached;
            }

            SKSvg? loaded = null;
            using Stream? stream = _resourceAssembly.GetManifestResourceStream(SvgIconConstants.ResourcePrefix + iconName);
            if (stream != null)
            {
                loaded = new SKSvg();
                loaded.Load(stream);
            }

            _cache[iconName] = loaded;
            return loaded;
        }
    }
}
