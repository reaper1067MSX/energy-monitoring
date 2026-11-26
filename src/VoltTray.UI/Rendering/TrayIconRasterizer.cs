using VoltTray.Core.Telemetry;
using VoltTray.UI.Interop;
using VoltTray.UI.Tokens;
using VoltTray.UI.Tokens.Primitives;
using VoltTray.UI.Tokens.Themes;

namespace VoltTray.UI.Rendering;

/// <summary>
/// Pure 32-bpp ARGB pixel rasterizer for Windows 11 24H2 system tray icons.
/// Produces transparent backgrounds, crisp 16/20/24/32 px battery glyphs,
/// taskbar-theme-aware colors, and distinct shapes per operational state.
/// </summary>
public static class TrayIconRasterizer
{
    public static int ResolvePhysicalSize(int dpi)
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

    public static uint[] RenderArgbPixels(UpsOperationalState state, int batteryPct, int size, bool isLightTaskbar)
    {
        int clampedSize = Math.Max(LayoutTokens.TrayIconPx[0], size);
        uint[] pixels = new uint[clampedSize * clampedSize];

        ThemePalette palette = isLightTaskbar ? LightThemeTokens.Palette : DarkThemeTokens.Palette;
        uint outlineColor = ColorUtilities.ParseArgbColor(palette.Icon);
        uint accentColor = ColorUtilities.ParseArgbColor(DesignTokens.GetTrayStateColor(state, isLightTaskbar));

        int padX = Math.Max(1, clampedSize / 16);
        int nubWidth = Math.Max(1, clampedSize / 8);
        int bodyLeft = padX;
        int bodyRight = clampedSize - padX - nubWidth - 1;
        int bodyTop = Math.Max(2, (clampedSize * 3) / 16);
        int bodyBottom = clampedSize - bodyTop - 1;

        bool dashedOutline = state == UpsOperationalState.CommunicationsLost;
        DrawBatteryOutline(pixels, clampedSize, bodyLeft, bodyTop, bodyRight, bodyBottom, nubWidth, outlineColor, dashedOutline);

        int innerLeft = bodyLeft + 2;
        int innerRight = bodyRight - 2;
        int innerTop = bodyTop + 2;
        int innerBottom = bodyBottom - 2;

        switch (state)
        {
            case UpsOperationalState.LineNormal:
                FillBatteryBar(pixels, clampedSize, innerLeft, innerTop, innerRight, innerBottom, batteryPct, accentColor);
                CarveAcBoltCutout(pixels, clampedSize, innerLeft, innerTop, innerRight, innerBottom);
                break;

            case UpsOperationalState.OnBattery:
            case UpsOperationalState.BatteryLow:
                FillBatteryBar(pixels, clampedSize, innerLeft, innerTop, innerRight, innerBottom, batteryPct, accentColor);
                break;

            case UpsOperationalState.ChargingAvr:
                DrawAvrWave(pixels, clampedSize, innerLeft, innerTop, innerRight, innerBottom, accentColor);
                break;

            case UpsOperationalState.Fault:
                DrawExclamationGlyph(pixels, clampedSize, innerLeft, innerTop, innerRight, innerBottom, accentColor);
                break;

            case UpsOperationalState.Paused:
                DrawPauseBars(pixels, clampedSize, innerLeft, innerTop, innerRight, innerBottom, accentColor);
                break;

            default:
                DrawDiagonalSlash(pixels, clampedSize, bodyLeft, bodyTop, bodyRight, bodyBottom, accentColor);
                break;
        }

        return pixels;
    }

    private static void DrawBatteryOutline(
        uint[] pixels,
        int size,
        int left,
        int top,
        int right,
        int bottom,
        int nubWidth,
        uint color,
        bool dashed)
    {
        for (int x = left; x <= right; x++)
        {
            if (!dashed || (x & 1) == 0)
            {
                SetPixel(pixels, size, x, top, color);
                SetPixel(pixels, size, x, bottom, color);
            }
        }

        for (int y = top; y <= bottom; y++)
        {
            if (!dashed || (y & 1) == 0)
            {
                SetPixel(pixels, size, left, y, color);
                SetPixel(pixels, size, right, y, color);
            }
        }

        int nubTop = top + Math.Max(2, (bottom - top) / 4);
        int nubBottom = bottom - Math.Max(2, (bottom - top) / 4);
        for (int x = right + 1; x <= right + nubWidth; x++)
        {
            for (int y = nubTop; y <= nubBottom; y++)
            {
                SetPixel(pixels, size, x, y, color);
            }
        }
    }

    private static void FillBatteryBar(
        uint[] pixels,
        int size,
        int left,
        int top,
        int right,
        int bottom,
        int batteryPct,
        uint color)
    {
        int clampedPct = Math.Clamp(batteryPct, 0, 100);
        int totalWidth = Math.Max(1, right - left + 1);
        int fillWidth = (int)Math.Round(totalWidth * (clampedPct / 100.0), MidpointRounding.AwayFromZero);
        if (clampedPct > 0 && fillWidth == 0)
        {
            fillWidth = 1;
        }

        int fillRight = Math.Min(right, left + fillWidth - 1);
        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= fillRight; x++)
            {
                SetPixel(pixels, size, x, y, color);
            }
        }
    }

    private static void CarveAcBoltCutout(uint[] pixels, int size, int left, int top, int right, int bottom)
    {
        int midX = (left + right) / 2;
        int midY = (top + bottom) / 2;
        for (int y = top; y <= bottom; y++)
        {
            int offset = y <= midY ? (midY - y) / 2 : -((y - midY) / 2);
            SetPixel(pixels, size, midX + offset, y, 0);
            SetPixel(pixels, size, midX + offset + 1, y, 0);
        }
    }

    private static void DrawAvrWave(uint[] pixels, int size, int left, int top, int right, int bottom, uint color)
    {
        int midY = (top + bottom) / 2;
        int amplitude = Math.Max(1, (bottom - top) / 3);
        int span = Math.Max(1, right - left);

        for (int x = left; x <= right; x++)
        {
            double phase = ((x - left) / (double)span) * Math.PI * 2.0;
            int y = midY - (int)Math.Round(Math.Sin(phase) * amplitude);
            SetPixel(pixels, size, x, y, color);
            SetPixel(pixels, size, x, Math.Min(bottom, y + 1), color);
        }
    }

    private static void DrawExclamationGlyph(uint[] pixels, int size, int left, int top, int right, int bottom, uint color)
    {
        int midX = (left + right) / 2;
        int dotY = bottom;
        int stemBottom = Math.Max(top, dotY - 2);

        for (int y = top; y <= stemBottom; y++)
        {
            SetPixel(pixels, size, midX, y, color);
            SetPixel(pixels, size, Math.Min(right, midX + 1), y, color);
        }

        SetPixel(pixels, size, midX, dotY, color);
        SetPixel(pixels, size, Math.Min(right, midX + 1), dotY, color);
    }

    private static void DrawPauseBars(uint[] pixels, int size, int left, int top, int right, int bottom, uint color)
    {
        int midX = (left + right) / 2;
        int leftBarX = Math.Max(left, midX - 2);
        int rightBarX = Math.Min(right, midX + 1);

        for (int y = top; y <= bottom; y++)
        {
            SetPixel(pixels, size, leftBarX, y, color);
            SetPixel(pixels, size, rightBarX, y, color);
            if (right - left >= 8)
            {
                SetPixel(pixels, size, leftBarX + 1, y, color);
                SetPixel(pixels, size, rightBarX + 1, y, color);
            }
        }
    }

    private static void DrawDiagonalSlash(uint[] pixels, int size, int left, int top, int right, int bottom, uint color)
    {
        int steps = Math.Max(1, right - left);
        for (int i = 0; i <= steps; i++)
        {
            int x = left + i;
            int y = bottom - (int)Math.Round((i / (double)steps) * (bottom - top));
            SetPixel(pixels, size, x, y, color);
        }
    }

    private static void SetPixel(uint[] pixels, int size, int x, int y, uint argb)
    {
        if (x >= 0 && x < size && y >= 0 && y < size)
        {
            pixels[(y * size) + x] = argb;
        }
    }
}
