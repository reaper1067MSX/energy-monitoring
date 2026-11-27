using System.Globalization;
using System.Text;
using VoltTray.Core.Telemetry;

namespace VoltTray.UI.Tray;

/// <summary>
/// Generates parameterized Circular Status Ring (Style D, 24x24 viewBox) SVG markup
/// for the compact 16x16 system tray icon mode.
/// </summary>
internal static class RingBatterySvgBuilder
{
    public static string BuildCacheKey(UpsOperationalState state, int batteryPct, bool isLightTaskbar)
    {
        int clampedPct = Math.Clamp(batteryPct, 0, 100);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"ring-d:{state}:{clampedPct}:{(isLightTaskbar ? "L" : "D")}");
    }

    public static string BuildIconSvg(UpsOperationalState state, int batteryPct, bool isLightTaskbar)
    {
        int clampedPct = Math.Clamp(batteryPct, 0, 100);
        string track = isLightTaskbar
            ? RingBatterySvgConstants.LightTrackColor
            : RingBatterySvgConstants.DarkTrackColor;
        string stroke = isLightTaskbar
            ? RingBatterySvgConstants.LightStrokeColor
            : RingBatterySvgConstants.DarkStrokeColor;
        string halo = isLightTaskbar
            ? RingBatterySvgConstants.LightHaloColor
            : RingBatterySvgConstants.DarkHaloColor;
        string accent = ResolveAccentColor(state, isLightTaskbar);

        double progress = state == UpsOperationalState.CommunicationsLost ? 0.0 : clampedPct / 100.0;
        double dashOffset = Math.Round(RingBatterySvgConstants.RingCircumference * (1.0 - progress), 2, MidpointRounding.AwayFromZero);

        var svg = new StringBuilder(512);
        svg.AppendFormat(
            CultureInfo.InvariantCulture,
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {0} {0}\" fill=\"none\">\n",
            RingBatterySvgConstants.ViewBoxSize);

        svg.AppendFormat(CultureInfo.InvariantCulture, RingBatterySvgConstants.TrackCircleFormat, track);
        svg.Append('\n');

        if (progress > 0.0)
        {
            svg.AppendFormat(
                CultureInfo.InvariantCulture,
                RingBatterySvgConstants.ProgressCircleFormat,
                accent,
                dashOffset.ToString("0.##", CultureInfo.InvariantCulture));
            svg.Append('\n');
        }

        string glyph = BuildCenterGlyphSvg(state, accent, stroke, halo);
        svg.Append(glyph);
        svg.Append('\n');
        svg.Append("</svg>");
        return svg.ToString();
    }

    private static string ResolveAccentColor(UpsOperationalState state, bool isLightTaskbar) => state switch
    {
        UpsOperationalState.LineNormal => isLightTaskbar
            ? RingBatterySvgConstants.LightOnlineColor
            : RingBatterySvgConstants.DarkOnlineColor,
        UpsOperationalState.OnBattery => isLightTaskbar
            ? RingBatterySvgConstants.LightOnBatteryColor
            : RingBatterySvgConstants.DarkOnBatteryColor,
        UpsOperationalState.BatteryLow or UpsOperationalState.Fault => isLightTaskbar
            ? RingBatterySvgConstants.LightCriticalColor
            : RingBatterySvgConstants.DarkCriticalColor,
        UpsOperationalState.ChargingAvr => isLightTaskbar
            ? RingBatterySvgConstants.LightAvrColor
            : RingBatterySvgConstants.DarkAvrColor,
        UpsOperationalState.Paused => isLightTaskbar
            ? RingBatterySvgConstants.LightPausedColor
            : RingBatterySvgConstants.DarkPausedColor,
        _ => isLightTaskbar
            ? RingBatterySvgConstants.LightStrokeColor
            : RingBatterySvgConstants.DarkStrokeColor
    };

    private static string BuildCenterGlyphSvg(UpsOperationalState state, string accent, string stroke, string halo) => state switch
    {
        UpsOperationalState.LineNormal => string.Format(
            CultureInfo.InvariantCulture,
            RingBatterySvgConstants.OnlineBoltGlyphFormat,
            accent),
        UpsOperationalState.OnBattery => string.Format(
            CultureInfo.InvariantCulture,
            RingBatterySvgConstants.OnBatteryGlyphFormat,
            accent),
        UpsOperationalState.BatteryLow => string.Format(
            CultureInfo.InvariantCulture,
            RingBatterySvgConstants.LowBatteryGlyphFormat,
            accent),
        UpsOperationalState.ChargingAvr => string.Format(
            CultureInfo.InvariantCulture,
            RingBatterySvgConstants.AvrWaveGlyphFormat,
            accent),
        UpsOperationalState.Fault => string.Format(
            CultureInfo.InvariantCulture,
            RingBatterySvgConstants.FaultTriangleGlyphFormat,
            accent,
            halo),
        UpsOperationalState.Paused => string.Format(
            CultureInfo.InvariantCulture,
            RingBatterySvgConstants.PausedBarsGlyphFormat,
            accent),
        _ => string.Format(
            CultureInfo.InvariantCulture,
            RingBatterySvgConstants.NoCommCrossGlyphFormat,
            stroke)
    };
}
