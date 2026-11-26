using VoltTray.UI.Atoms;
using VoltTray.UI.Atoms.TrayGlyphs;
using VoltTray.Core.Telemetry;
using VoltTray.UI.Interop;
using VoltTray.UI.Models;
using VoltTray.UI.Tokens;
using VoltTray.UI.Tokens.Primitives;
using VoltTray.UI.Tokens.Themes;

namespace VoltTray.UI.Tray;

/// <summary>
/// Composes a 32-bpp ARGB tray icon by delegating state-specific glyph drawing
/// to injected ITrayStateGlyphRenderer strategies over a PixelBuffer.
/// </summary>
public sealed class TrayIconRasterizer : ITrayIconRasterizer
{
    private readonly IReadOnlyDictionary<UpsOperationalState, ITrayStateGlyphRenderer> _glyphRenderers;
    private readonly ITrayStateGlyphRenderer _fallbackRenderer;

    public TrayIconRasterizer(
        IEnumerable<ITrayStateGlyphRenderer> glyphRenderers,
        ITrayStateGlyphRenderer fallbackRenderer)
    {
        ArgumentNullException.ThrowIfNull(glyphRenderers);
        _fallbackRenderer = fallbackRenderer ?? throw new ArgumentNullException(nameof(fallbackRenderer));

        var map = new Dictionary<UpsOperationalState, ITrayStateGlyphRenderer>();
        foreach (ITrayStateGlyphRenderer renderer in glyphRenderers)
        {
            foreach (UpsOperationalState state in renderer.SupportedStates)
            {
                map[state] = renderer;
            }
        }

        _glyphRenderers = map;
    }

    public static TrayIconRasterizer CreateDefault()
    {
        var fallback = new NoCommTrayGlyphRenderer();
        return new TrayIconRasterizer(
        [
            new OnlineTrayGlyphRenderer(),
            new BatteryLevelTrayGlyphRenderer(),
            new AvrWaveTrayGlyphRenderer(),
            new FaultTrayGlyphRenderer(),
            new PausedTrayGlyphRenderer(),
            fallback
        ],
        fallback);
    }

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
        int clampedSize = Math.Max(LayoutTokens.TrayIconPx[0], size);
        var buffer = new PixelBuffer(clampedSize);

        ThemePalette palette = isLightTaskbar ? LightThemeTokens.Palette : DarkThemeTokens.Palette;
        uint outlineColor = ColorUtilities.ParseArgbColor(palette.Icon);
        uint accentColor = ColorUtilities.ParseArgbColor(DesignTokens.GetTrayStateColor(state, isLightTaskbar));

        ITrayStateGlyphRenderer glyphRenderer = _glyphRenderers.GetValueOrDefault(state, _fallbackRenderer);

        int padX = Math.Max(1, clampedSize / 16);
        int nubWidth = Math.Max(1, clampedSize / 8);
        int bodyTop = Math.Max(2, (clampedSize * 3) / 16);
        var bodyBounds = new ViewRect(padX, bodyTop, clampedSize - padX - nubWidth - 1, clampedSize - bodyTop - 1);
        var innerBounds = new ViewRect(bodyBounds.Left + 2, bodyBounds.Top + 2, bodyBounds.Right - 2, bodyBounds.Bottom - 2);

        buffer.DrawOutline(bodyBounds, outlineColor, glyphRenderer.UsesDashedOutline);

        int nubTop = bodyBounds.Top + Math.Max(2, bodyBounds.Height / 4);
        int nubBottom = bodyBounds.Bottom - Math.Max(2, bodyBounds.Height / 4);
        buffer.FillRect(bodyBounds.Right + 1, nubTop, bodyBounds.Right + nubWidth, nubBottom, outlineColor);

        glyphRenderer.Render(buffer, bodyBounds, innerBounds, batteryPct, accentColor);
        return buffer.Pixels;
    }
}
