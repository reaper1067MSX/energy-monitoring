using System.Globalization;
using System.Runtime.InteropServices;
using SkiaSharp;
using VoltTray.Core.Telemetry;
using VoltTray.UI.Atoms;
using VoltTray.UI.Interop;
using VoltTray.UI.Tokens;
using VoltTray.UI.Tokens.Primitives;
using VoltTray.UI.Tokens.Themes;
using VoltTray.UI.Tray;

namespace VoltTray.UI.TaskbarPill;

/// <summary>
/// Renders a crisp, anti-aliased 32-bpp premultiplied ARGB TrafficMonitor-style taskbar pill
/// using SkiaSharp and embedded SVG state icons over an alpha >= 1 hit-testable interior.
/// </summary>
internal sealed class TaskbarPillSurfaceRenderer : ITaskbarPillSurfaceRenderer, IDisposable
{
    private readonly ITrayIconRasterizer _iconRasterizer;
    private readonly ISvgIconLibrary _svgIconLibrary;
    private readonly bool _ownsSvgIconLibrary;
    private readonly SKTypeface _regularTypeface;
    private readonly SKTypeface _semiBoldTypeface;
    private bool _disposed;

    public TaskbarPillSurfaceRenderer(ITrayIconRasterizer iconRasterizer, ISvgIconLibrary? svgIconLibrary = null)
    {
        _iconRasterizer = iconRasterizer ?? throw new ArgumentNullException(nameof(iconRasterizer));
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

        _regularTypeface = ResolveTypeface(SKFontStyleWeight.Normal);
        _semiBoldTypeface = ResolveTypeface(SKFontStyleWeight.SemiBold);
    }

    public uint[] RenderArgbPixels(UpsTelemetry telemetry, int width, int height, bool isLightTaskbar, int dpi)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        using SKSurface surface = RenderSurface(telemetry, width, height, isLightTaskbar, dpi);
        var imageInfo = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        uint[] pixels = new uint[width * height];

        GCHandle handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            surface.ReadPixels(imageInfo, handle.AddrOfPinnedObject(), width * sizeof(uint), 0, 0);
        }
        finally
        {
            handle.Free();
        }

        for (int i = 0; i < pixels.Length; i++)
        {
            if ((pixels[i] >> 24) == 0)
            {
                pixels[i] = Win32UiConstants.PillHitTestAlpha;
            }
        }

        return pixels;
    }

    public byte[] RenderPng(UpsTelemetry telemetry, int width, int height, bool isLightTaskbar, int dpi)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        using SKSurface surface = RenderSurface(telemetry, width, height, isLightTaskbar, dpi);
        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, TaskbarPillLayoutConstants.PngQuality);
        return data.ToArray();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _regularTypeface.Dispose();
        _semiBoldTypeface.Dispose();

        if (_ownsSvgIconLibrary)
        {
            _svgIconLibrary.Dispose();
        }
    }

    private SKSurface RenderSurface(UpsTelemetry telemetry, int width, int height, bool isLightTaskbar, int dpi)
    {
        _ = _iconRasterizer.ResolvePhysicalSize(dpi);
        var imageInfo = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        SKSurface surface = SKSurface.Create(imageInfo)
            ?? throw new InvalidOperationException("Failed to allocate SkiaSharp taskbar pill surface.");

        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(0, 0, 0, TaskbarPillLayoutConstants.HitTestAlpha));

        float scale = Math.Max(TaskbarPillLayoutConstants.MinScale, dpi / (float)LayoutTokens.DefaultDpi);
        ThemePalette palette = isLightTaskbar ? LightThemeTokens.Palette : DarkThemeTokens.Palette;
        SKColor accentColor = SKColor.Parse(DesignTokens.GetTrayStateColor(telemetry.State, isLightTaskbar));
        SKColor surfaceColor = SKColor.Parse(palette.Surface)
            .WithAlpha(isLightTaskbar ? TaskbarPillLayoutConstants.LightFillAlpha : TaskbarPillLayoutConstants.DarkFillAlpha);
        SKColor borderColor = SKColor.Parse(palette.Border).WithAlpha(TaskbarPillLayoutConstants.BorderAlpha);

        float insetX = TaskbarPillLayoutConstants.HorizontalInsetPx * scale;
        float insetY = TaskbarPillLayoutConstants.VerticalInsetPx * scale;
        var pillRect = new SKRect(insetX, insetY, Math.Max(insetX + 4f, width - insetX), Math.Max(insetY + 4f, height - insetY));
        float radius = TaskbarPillLayoutConstants.CornerRadiusPx * scale;

        using var fillPaint = new SKPaint { Color = surfaceColor, IsAntialias = true };
        using var borderPaint = new SKPaint
        {
            Color = borderColor,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = TaskbarPillLayoutConstants.BorderStrokeWidth * scale,
            IsAntialias = true
        };

        canvas.DrawRoundRect(pillRect, radius, radius, fillPaint);
        canvas.DrawRoundRect(pillRect, radius, radius, borderPaint);

        float padX = TaskbarPillLayoutConstants.PaddingX * scale;
        float iconHeight = Math.Min(TaskbarPillLayoutConstants.IconHeightPx * scale, pillRect.Height - (4f * scale));
        float iconWidth = TaskbarPillLayoutConstants.IconWidthPx * scale;
        float iconLeft = pillRect.Left + padX;
        float iconTop = pillRect.MidY - (iconHeight / 2f);
        var iconRect = new SKRect(iconLeft, iconTop, iconLeft + iconWidth, iconTop + iconHeight);

        string iconCacheKey = FluentBatterySvgBuilder.BuildCacheKey(telemetry.State, telemetry.BatteryPercentage, isLightTaskbar);
        string iconSvgMarkup = FluentBatterySvgBuilder.BuildIconSvg(telemetry.State, telemetry.BatteryPercentage, isLightTaskbar);
        _svgIconLibrary.TryDrawMarkup(canvas, iconCacheKey, iconSvgMarkup, iconRect);

        float textLeft = iconRect.Right + (TaskbarPillLayoutConstants.IconTextGapPx * scale);
        float contentRight = pillRect.Right - padX;
        if (contentRight <= textLeft + (8f * scale))
        {
            return surface;
        }

        string primaryText = FormatPrimaryText(telemetry);
        using var primaryFont = new SKFont(_semiBoldTypeface, TaskbarPillLayoutConstants.PrimaryFontSize * scale);
        using var primaryPaint = new SKPaint { Color = SKColor.Parse(palette.Text), IsAntialias = true };

        SKFontMetrics metrics = primaryFont.Metrics;
        float baseline = pillRect.MidY - ((metrics.Ascent + metrics.Descent) / 2f);
        canvas.DrawText(primaryText, textLeft, baseline, SKTextAlign.Left, primaryFont, primaryPaint);

        return surface;
    }

    private static string FormatPrimaryText(UpsTelemetry telemetry)
    {
        return telemetry.State switch
        {
            UpsOperationalState.Fault => TaskbarPillLayoutConstants.FaultShortText,
            UpsOperationalState.Paused => TaskbarPillLayoutConstants.PausedShortText,
            UpsOperationalState.CommunicationsLost => TaskbarPillLayoutConstants.NoCommShortText,
            _ => string.Create(CultureInfo.InvariantCulture, $"{Math.Clamp(telemetry.BatteryPercentage, 0, 100)}%")
        };
    }

    private static SKTypeface ResolveTypeface(SKFontStyleWeight weight)
    {
        var style = new SKFontStyle(weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        return SKTypeface.FromFamilyName(TaskbarPillLayoutConstants.PrimaryFontFamily, style)
            ?? SKTypeface.FromFamilyName(TaskbarPillLayoutConstants.FallbackFontFamily, style)
            ?? SKTypeface.Default;
    }
}
