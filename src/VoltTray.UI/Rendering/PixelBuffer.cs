using VoltTray.UI.Models;

namespace VoltTray.UI.Rendering;

/// <summary>
/// Lightweight 32-bpp ARGB pixel drawing helper shared by tray glyph renderers.
/// </summary>
public sealed class PixelBuffer
{
    public int Size { get; }
    public uint[] Pixels { get; }

    public PixelBuffer(int size)
    {
        Size = size;
        Pixels = new uint[size * size];
    }

    public void SetPixel(int x, int y, uint argb)
    {
        if (x >= 0 && x < Size && y >= 0 && y < Size)
        {
            Pixels[(y * Size) + x] = argb;
        }
    }

    public void FillRect(int left, int top, int right, int bottom, uint argb)
    {
        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                SetPixel(x, y, argb);
            }
        }
    }

    public void FillBatteryBar(ViewRect bounds, int batteryPct, uint argb)
    {
        int clampedPct = Math.Clamp(batteryPct, 0, 100);
        int totalWidth = Math.Max(1, bounds.Width + 1);
        int fillWidth = (int)Math.Round(totalWidth * (clampedPct / 100.0), MidpointRounding.AwayFromZero);
        if (clampedPct > 0 && fillWidth == 0)
        {
            fillWidth = 1;
        }

        int fillRight = Math.Min(bounds.Right, bounds.Left + fillWidth - 1);
        FillRect(bounds.Left, bounds.Top, fillRight, bounds.Bottom, argb);
    }

    public void DrawOutline(ViewRect bounds, uint argb, bool dashed)
    {
        for (int x = bounds.Left; x <= bounds.Right; x++)
        {
            if (!dashed || (x & 1) == 0)
            {
                SetPixel(x, bounds.Top, argb);
                SetPixel(x, bounds.Bottom, argb);
            }
        }

        for (int y = bounds.Top; y <= bounds.Bottom; y++)
        {
            if (!dashed || (y & 1) == 0)
            {
                SetPixel(bounds.Left, y, argb);
                SetPixel(bounds.Right, y, argb);
            }
        }
    }

    public void DrawDiagonalLine(int x0, int y0, int x1, int y1, uint argb)
    {
        int steps = Math.Max(1, Math.Abs(x1 - x0));
        for (int i = 0; i <= steps; i++)
        {
            double t = i / (double)steps;
            int x = x0 + (int)Math.Round((x1 - x0) * t);
            int y = y0 + (int)Math.Round((y1 - y0) * t);
            SetPixel(x, y, argb);
        }
    }
}
