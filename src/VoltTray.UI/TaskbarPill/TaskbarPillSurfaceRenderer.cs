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
public sealed class TaskbarPillSurfaceRenderer : ITaskbarPillSurfaceRenderer, IDisposable
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
        float iconSize = Math.Min(TaskbarPillLayoutConstants.IconSizePx * scale, pillRect.Height - (4f * scale));
        float iconLeft = pillRect.Left + padX;
        float iconTop = pillRect.MidY - (iconSize / 2f);
        var iconRect = new SKRect(iconLeft, iconTop, iconLeft + iconSize, iconTop + iconSize);

        string iconName = ResolveStateIcon(telemetry.State);
        _svgIconLibrary.TryDraw(canvas, iconName, iconRect, accentColor);

        float textLeft = iconRect.Right + (TaskbarPillLayoutConstants.IconTextGapPx * scale);
        float contentRight = pillRect.Right - padX;
        if (contentRight <= textLeft + (8f * scale))
        {
            return surface;
        }

        string primaryText = FormatPrimaryText(telemetry);
        string secondaryText = string.Create(CultureInfo.InvariantCulture, $" · {Math.Clamp(telemetry.LoadPercentage, 0, 100)}%");

        using var primaryFont = new SKFont(_semiBoldTypeface, TaskbarPillLayoutConstants.PrimaryFontSize * scale);
        using var secondaryFont = new SKFont(_regularTypeface, TaskbarPillLayoutConstants.SecondaryFontSize * scale);
        using var primaryPaint = new SKPaint { Color = SKColor.Parse(palette.Text), IsAntialias = true };
        using var secondaryPaint = new SKPaint { Color = SKColor.Parse(palette.TextMuted), IsAntialias = true };

        float barHeight = TaskbarPillLayoutConstants.BarHeightPx * scale;
        float barGap = TaskbarPillLayoutConstants.BarTopGapPx * scale;
        float textCenterY = pillRect.MidY - ((barHeight + barGap) / 2f) - (TaskbarPillLayoutConstants.TextVerticalBiasPx * scale);
        SKFontMetrics metrics = primaryFont.Metrics;
        float baseline = textCenterY - ((metrics.Ascent + metrics.Descent) / 2f);

        canvas.DrawText(primaryText, textLeft, baseline, SKTextAlign.Left, primaryFont, primaryPaint);

        float primaryWidth = primaryFont.MeasureText(primaryText);
        float secondaryX = textLeft + primaryWidth;
        float secondaryWidth = secondaryFont.MeasureText(secondaryText);
        if (secondaryX + secondaryWidth <= contentRight)
        {
            canvas.DrawText(secondaryText, secondaryX, baseline, SKTextAlign.Left, secondaryFont, secondaryPaint);
        }

        float barTop = textCenterY + (primaryFont.Size * 0.45f) + barGap;
        barTop = Math.Min(barTop, pillRect.Bottom - barHeight - (4f * scale));
        var trackRect = new SKRect(textLeft, barTop, contentRight, barTop + barHeight);
        float barRadius = TaskbarPillLayoutConstants.BarCornerRadiusPx * scale;

        using var trackPaint = new SKPaint
        {
            Color = SKColor.Parse(palette.Border).WithAlpha(TaskbarPillLayoutConstants.TrackAlpha),
            IsAntialias = true
        };
        canvas.DrawRoundRect(trackRect, barRadius, barRadius, trackPaint);

        int battery = Math.Clamp(telemetry.BatteryPercentage, 0, 100);
        if (battery > 0 && telemetry.State != UpsOperationalState.CommunicationsLost)
        {
            float fillWidth = Math.Max(barHeight, trackRect.Width * (battery / 100f));
            var barFillRect = new SKRect(
                trackRect.Left,
                trackRect.Top,
                Math.Min(trackRect.Right, trackRect.Left + fillWidth),
                trackRect.Bottom);
            using var barFillPaint = new SKPaint { Color = accentColor, IsAntialias = true };
            canvas.DrawRoundRect(barFillRect, barRadius, barRadius, barFillPaint);
        }

        return surface;
    }

    private static string ResolveStateIcon(UpsOperationalState state)
    {
        return state switch
        {
            UpsOperationalState.LineNormal => SvgIconConstants.TrayOnline,
            UpsOperationalState.OnBattery or UpsOperationalState.BatteryLow => SvgIconConstants.TrayOnBattery,
            UpsOperationalState.ChargingAvr => SvgIconConstants.TrayAvr,
            UpsOperationalState.Fault => SvgIconConstants.TrayFault,
            UpsOperationalState.Paused => SvgIconConstants.ActionPause,
            _ => SvgIconConstants.TrayNoComm
        };
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
