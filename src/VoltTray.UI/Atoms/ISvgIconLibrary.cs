using SkiaSharp;

namespace VoltTray.UI.Atoms;

public interface ISvgIconLibrary : IDisposable
{
    bool TryDraw(SKCanvas canvas, string iconName, SKRect destination, SKColor? tintColor = null);

    bool TryDrawMarkup(SKCanvas canvas, string cacheKey, string svgMarkup, SKRect destination);

    uint[]? RenderArgbPixels(string iconName, int size, SKColor? tintColor = null);
}
