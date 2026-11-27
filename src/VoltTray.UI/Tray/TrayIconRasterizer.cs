using System.Globalization;
using System.Runtime.InteropServices;
using SkiaSharp;
using VoltTray.Core.Telemetry;
using VoltTray.UI.Atoms;
using VoltTray.UI.Interop;
using VoltTray.UI.Tokens;
using VoltTray.UI.Tokens.Primitives;
using VoltTray.UI.Tokens.Themes;

namespace VoltTray.UI.Tray;

/// <summary>
/// Renders anti-aliased 32-bpp premultiplied ARGB tray icons using SkiaSharp and embedded
/// handoff SVG geometry, adapting stroke/accent contrast to Light and Dark taskbars.
/// </summary>
internal sealed class TrayIconRasterizer : ITrayIconRasterizer, IDisposable
{
    private readonly ISvgIconLibrary _svgIconLibrary;
    private readonly bool _ownsSvgIconLibrary;
    private readonly SKTypeface _boldTypeface;
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

        var boldStyle = new SKFontStyle(SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        _boldTypeface = SKTypeface.FromFamilyName(TrayIconRenderConstants.PrimaryFontFamily, boldStyle)
            ?? SKTypeface.FromFamilyName(TrayIconRenderConstants.FallbackFontFamily, boldStyle)
            ?? SKTypeface.Default;
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
        _boldTypeface.Dispose();

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

        float scale = size / TrayIconRenderConstants.SvgViewBoxSize;
        ThemePalette palette = isLightTaskbar ? LightThemeTokens.Palette : DarkThemeTokens.Palette;
        SKColor outlineColor = SKColor.Parse(palette.Icon);
        SKColor accentColor = SKColor.Parse(DesignTokens.GetTrayStateColor(state, isLightTaskbar));

        switch (state)
        {
            case UpsOperationalState.LineNormal:
                DrawOnlineIcon(canvas, scale, outlineColor, accentColor, batteryPct);
                break;
            case UpsOperationalState.OnBattery:
            case UpsOperationalState.BatteryLow:
                DrawOnBatteryBadge(canvas, scale, accentColor, SKColor.Parse(palette.OnWarning), batteryPct);
                break;
            case UpsOperationalState.ChargingAvr:
                DrawAvrIcon(canvas, scale, outlineColor, accentColor);
                break;
            case UpsOperationalState.Fault:
                _svgIconLibrary.TryDraw(canvas, SvgIconConstants.TrayFault, new SKRect(0, 0, size, size));
                break;
            case UpsOperationalState.Paused:
                DrawPausedIcon(canvas, scale, outlineColor, accentColor);
                break;
            default:
                DrawNoCommIcon(canvas, scale, outlineColor, SKColor.Parse(palette.TextMuted));
                break;
        }

        return surface;
    }

    private static void DrawOnlineIcon(SKCanvas canvas, float scale, SKColor outlineColor, SKColor accentColor, int batteryPct)
    {
        DrawBatteryShell(canvas, scale, outlineColor, dashed: false);

        int bars = batteryPct switch
        {
            >= 67 => 3,
            >= 34 => 2,
            > 0 => 1,
            _ => 0
        };

        using var barPaint = new SKPaint { Color = accentColor, IsAntialias = true };
        float[] barXs = [4.4f, 8.8f, 13.2f];
        for (int i = 0; i < bars; i++)
        {
            var barRect = new SKRect(barXs[i] * scale, 9f * scale, (barXs[i] + 3.4f) * scale, 15f * scale);
            canvas.DrawRoundRect(barRect, 0.7f * scale, 0.7f * scale, barPaint);
        }
    }

    private void DrawOnBatteryBadge(SKCanvas canvas, float scale, SKColor badgeColor, SKColor textColor, int batteryPct)
    {
        var badgeRect = new SKRect(1.5f * scale, 4f * scale, 22.5f * scale, 20f * scale);
        using var fillPaint = new SKPaint { Color = badgeColor, IsAntialias = true };
        canvas.DrawRoundRect(badgeRect, 3f * scale, 3f * scale, fillPaint);

        string text = Math.Clamp(batteryPct, 0, 99).ToString(CultureInfo.InvariantCulture);
        using var font = new SKFont(_boldTypeface, 12.2f * scale);
        using var textPaint = new SKPaint { Color = textColor, IsAntialias = true };
        SKFontMetrics metrics = font.Metrics;
        float baseline = badgeRect.MidY - ((metrics.Ascent + metrics.Descent) / 2f);
        canvas.DrawText(text, badgeRect.MidX, baseline, SKTextAlign.Center, font, textPaint);
    }

    private static void DrawAvrIcon(SKCanvas canvas, float scale, SKColor outlineColor, SKColor accentColor)
    {
        DrawBatteryShell(canvas, scale, outlineColor, dashed: false);

        using var wavePaint = new SKPaint
        {
            Color = accentColor,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1.2f, 1.6f * scale),
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true
        };

        using var builder = new SKPathBuilder();
        builder.MoveTo(4.5f * scale, 12f * scale);
        builder.CubicTo(6f * scale, 9f * scale, 7.5f * scale, 9f * scale, 8.7f * scale, 12f * scale);
        builder.CubicTo(9.9f * scale, 15f * scale, 11.4f * scale, 15f * scale, 12.9f * scale, 12f * scale);
        builder.CubicTo(14.1f * scale, 10f * scale, 15.1f * scale, 10f * scale, 16.2f * scale, 10.8f * scale);
        using SKPath path = builder.Detach();
        canvas.DrawPath(path, wavePaint);
    }

    private void DrawPausedIcon(SKCanvas canvas, float scale, SKColor outlineColor, SKColor accentColor)
    {
        DrawBatteryShell(canvas, scale, outlineColor, dashed: false);

        var pauseBounds = new SKRect(6f * scale, 7.5f * scale, 15f * scale, 16.5f * scale);
        _svgIconLibrary.TryDraw(canvas, SvgIconConstants.ActionPause, pauseBounds, accentColor);
    }

    private static void DrawNoCommIcon(SKCanvas canvas, float scale, SKColor slashColor, SKColor mutedColor)
    {
        DrawBatteryShell(canvas, scale, mutedColor, dashed: true);

        using var slashPaint = new SKPaint
        {
            Color = slashColor,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1.3f, 1.8f * scale),
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true
        };

        canvas.DrawLine(4f * scale, 20f * scale, 20f * scale, 4f * scale, slashPaint);
    }

    private static void DrawBatteryShell(SKCanvas canvas, float scale, SKColor color, bool dashed)
    {
        using var strokePaint = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1.2f, 1.6f * scale),
            IsAntialias = true
        };

        if (dashed)
        {
            strokePaint.PathEffect = SKPathEffect.CreateDash([3f * scale, 2f * scale], 0f);
        }

        var bodyRect = new SKRect(2f * scale, 6.5f * scale, 19f * scale, 17.5f * scale);
        canvas.DrawRoundRect(bodyRect, 2f * scale, 2f * scale, strokePaint);

        using var nubPaint = new SKPaint { Color = color, IsAntialias = true };
        var nubRect = new SKRect(20f * scale, 10.5f * scale, 22f * scale, 13.5f * scale);
        canvas.DrawRoundRect(nubRect, 1f * scale, 1f * scale, nubPaint);
    }
}
