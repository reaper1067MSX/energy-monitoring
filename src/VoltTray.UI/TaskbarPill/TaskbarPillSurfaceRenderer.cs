using VoltTray.Core.Telemetry;
using VoltTray.UI.Interop;
using VoltTray.UI.Tokens;
using VoltTray.UI.Tokens.Themes;
using VoltTray.UI.Tray;

namespace VoltTray.UI.TaskbarPill;

/// <summary>
/// Renders a 32-bpp premultiplied ARGB taskbar pill surface composing the atomic tray glyph
/// over an alpha >= 1 hit-testable interior so mouse clicks never pass through.
/// </summary>
public sealed class TaskbarPillSurfaceRenderer : ITaskbarPillSurfaceRenderer
{
    private readonly ITrayIconRasterizer _iconRasterizer;

    public TaskbarPillSurfaceRenderer(ITrayIconRasterizer iconRasterizer)
    {
        _iconRasterizer = iconRasterizer ?? throw new ArgumentNullException(nameof(iconRasterizer));
    }

    public uint[] RenderArgbPixels(UpsTelemetry telemetry, int width, int height, bool isLightTaskbar, int dpi)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        uint[] surface = new uint[width * height];
        Array.Fill(surface, Win32UiConstants.PillHitTestAlpha);

        ThemePalette palette = isLightTaskbar ? LightThemeTokens.Palette : DarkThemeTokens.Palette;
        uint borderColor = ColorUtilities.ParsePremultipliedArgbColor(palette.Border, Win32UiConstants.PillBorderAlpha);
        uint accentColor = ColorUtilities.ParsePremultipliedArgbColor(DesignTokens.GetTrayStateColor(telemetry.State, isLightTaskbar));

        DrawPillBorder(surface, width, height, borderColor);

        int iconSize = _iconRasterizer.ResolvePhysicalSize(dpi);
        uint[] iconPixels = _iconRasterizer.RenderArgbPixels(telemetry.State, telemetry.BatteryPercentage, iconSize, isLightTaskbar);
        int iconOffsetX = Win32UiConstants.PillEdgeGapPx;
        int iconOffsetY = Math.Max(0, (height - iconSize) / 2);
        BlitOpaquePixels(surface, width, height, iconPixels, iconSize, iconOffsetX, iconOffsetY);

        int barLeft = Math.Min(width - 1, iconOffsetX + iconSize + Win32UiConstants.PillEdgeGapPx);
        int barRight = Math.Max(barLeft, width - Win32UiConstants.PillEdgeGapPx - 1);
        int barTop = Math.Clamp((height / 2) - Win32UiConstants.PillBarHalfHeightPx, 0, height - 1);
        int barBottom = Math.Clamp(barTop + Win32UiConstants.PillBarHeightPx, barTop, height - 1);
        int fillWidth = (int)Math.Round((barRight - barLeft + 1) * (Math.Clamp(telemetry.BatteryPercentage, 0, 100) / 100.0));
        int fillRight = Math.Clamp(barLeft + Math.Max(1, fillWidth) - 1, barLeft, barRight);

        for (int y = barTop; y <= barBottom; y++)
        {
            for (int x = barLeft; x <= fillRight; x++)
            {
                surface[(y * width) + x] = accentColor;
            }
        }

        return surface;
    }

    private static void DrawPillBorder(uint[] surface, int width, int height, uint color)
    {
        for (int x = 0; x < width; x++)
        {
            surface[x] = color;
            surface[((height - 1) * width) + x] = color;
        }

        for (int y = 0; y < height; y++)
        {
            surface[y * width] = color;
            surface[(y * width) + (width - 1)] = color;
        }
    }

    private static void BlitOpaquePixels(
        uint[] destination,
        int destWidth,
        int destHeight,
        uint[] source,
        int sourceSize,
        int offsetX,
        int offsetY)
    {
        for (int y = 0; y < sourceSize; y++)
        {
            int destY = offsetY + y;
            if (destY < 0 || destY >= destHeight)
            {
                continue;
            }

            for (int x = 0; x < sourceSize; x++)
            {
                int destX = offsetX + x;
                if (destX < 0 || destX >= destWidth)
                {
                    continue;
                }

                uint pixel = source[(y * sourceSize) + x];
                if ((pixel >> 24) != 0)
                {
                    destination[(destY * destWidth) + destX] = pixel;
                }
            }
        }
    }
}
