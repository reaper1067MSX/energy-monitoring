using System.Globalization;
using System.Text;
using VoltTray.Core.Telemetry;

namespace VoltTray.UI.Tray;

/// <summary>
/// Generates parameterized Fluent Tray (Style A, 28x16 viewBox) SVG markup matching the
/// ups_battery_svg_studio_icon.html specification with Light/Dark contrast halos.
/// </summary>
internal static class FluentBatterySvgBuilder
{
    public static string BuildCacheKey(UpsOperationalState state, int batteryPct, bool isLightTaskbar)
    {
        int clampedPct = Math.Clamp(batteryPct, 0, 100);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"fluent-a:{state}:{clampedPct}:{(isLightTaskbar ? "L" : "D")}");
    }

    public static string BuildIconSvg(UpsOperationalState state, int batteryPct, bool isLightTaskbar)
    {
        int clampedPct = Math.Clamp(batteryPct, 0, 100);
        string stroke = isLightTaskbar
            ? FluentBatterySvgConstants.LightStrokeColor
            : FluentBatterySvgConstants.DarkStrokeColor;
        string halo = isLightTaskbar
            ? FluentBatterySvgConstants.LightHaloColor
            : FluentBatterySvgConstants.DarkHaloColor;
        string fill = ResolveFillColor(state, isLightTaskbar, stroke);
        (string topStop, string bottomStop) = ResolveGradientStops(state, isLightTaskbar);

        double barWidth = state == UpsOperationalState.CommunicationsLost || clampedPct == 0
            ? 0.0
            : Math.Round(
                Math.Max(
                    FluentBatterySvgConstants.MinFillBarWidth,
                    (clampedPct / 100.0) * FluentBatterySvgConstants.MaxFillBarWidth),
                2,
                MidpointRounding.AwayFromZero);

        var svg = new StringBuilder(640);
        svg.AppendFormat(
            CultureInfo.InvariantCulture,
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {0} {1}\" fill=\"none\">\n",
            FluentBatterySvgConstants.ViewBoxWidth,
            FluentBatterySvgConstants.ViewBoxHeight);

        if (barWidth > 0.0)
        {
            svg.AppendFormat(CultureInfo.InvariantCulture, FluentBatterySvgConstants.GradientDefsFormat, topStop, bottomStop);
            svg.Append('\n');
        }

        svg.AppendFormat(CultureInfo.InvariantCulture, FluentBatterySvgConstants.ShellElementFormat, stroke);
        svg.Append('\n');

        if (barWidth > 0.0)
        {
            svg.AppendFormat(
                CultureInfo.InvariantCulture,
                FluentBatterySvgConstants.FillBarFormat,
                barWidth.ToString("0.##", CultureInfo.InvariantCulture));
            svg.Append('\n');

            double highlightWidth = Math.Round(Math.Max(0.8, barWidth - 0.8), 2, MidpointRounding.AwayFromZero);
            svg.AppendFormat(
                CultureInfo.InvariantCulture,
                FluentBatterySvgConstants.SpecularHighlightFormat,
                highlightWidth.ToString("0.##", CultureInfo.InvariantCulture));
            svg.Append('\n');
        }

        string? badge = BuildBadgeSvg(state, stroke, halo, fill);
        if (!string.IsNullOrEmpty(badge))
        {
            svg.Append(badge);
            svg.Append('\n');
        }

        svg.Append("</svg>");
        return svg.ToString();
    }

    private static (string Top, string Bottom) ResolveGradientStops(UpsOperationalState state, bool isLightTaskbar) => state switch
    {
        UpsOperationalState.LineNormal => isLightTaskbar
            ? (FluentBatterySvgConstants.LightOnlineTopColor, FluentBatterySvgConstants.LightOnlineBottomColor)
            : (FluentBatterySvgConstants.DarkOnlineTopColor, FluentBatterySvgConstants.DarkOnlineBottomColor),
        UpsOperationalState.OnBattery => isLightTaskbar
            ? (FluentBatterySvgConstants.LightOnBatteryTopColor, FluentBatterySvgConstants.LightOnBatteryBottomColor)
            : (FluentBatterySvgConstants.DarkOnBatteryTopColor, FluentBatterySvgConstants.DarkOnBatteryBottomColor),
        UpsOperationalState.BatteryLow or UpsOperationalState.Fault => isLightTaskbar
            ? (FluentBatterySvgConstants.LightCriticalTopColor, FluentBatterySvgConstants.LightCriticalBottomColor)
            : (FluentBatterySvgConstants.DarkCriticalTopColor, FluentBatterySvgConstants.DarkCriticalBottomColor),
        UpsOperationalState.ChargingAvr => isLightTaskbar
            ? (FluentBatterySvgConstants.LightAvrTopColor, FluentBatterySvgConstants.LightAvrBottomColor)
            : (FluentBatterySvgConstants.DarkAvrTopColor, FluentBatterySvgConstants.DarkAvrBottomColor),
        UpsOperationalState.Paused => isLightTaskbar
            ? (FluentBatterySvgConstants.LightPausedTopColor, FluentBatterySvgConstants.LightPausedBottomColor)
            : (FluentBatterySvgConstants.DarkPausedTopColor, FluentBatterySvgConstants.DarkPausedBottomColor),
        _ => (FluentBatterySvgConstants.DarkOnlineTopColor, FluentBatterySvgConstants.DarkOnlineBottomColor)
    };

    private static string ResolveFillColor(UpsOperationalState state, bool isLightTaskbar, string stroke) => state switch
    {
        UpsOperationalState.LineNormal => isLightTaskbar
            ? FluentBatterySvgConstants.LightOnlineGreenColor
            : FluentBatterySvgConstants.DarkOnlineGreenColor,
        UpsOperationalState.OnBattery => isLightTaskbar
            ? FluentBatterySvgConstants.LightOnBatteryColor
            : FluentBatterySvgConstants.DarkOnBatteryColor,
        UpsOperationalState.BatteryLow or UpsOperationalState.Fault => isLightTaskbar
            ? FluentBatterySvgConstants.LightCriticalColor
            : FluentBatterySvgConstants.DarkCriticalColor,
        UpsOperationalState.ChargingAvr => isLightTaskbar
            ? FluentBatterySvgConstants.LightAvrColor
            : FluentBatterySvgConstants.DarkAvrColor,
        UpsOperationalState.Paused => isLightTaskbar
            ? FluentBatterySvgConstants.LightPausedColor
            : FluentBatterySvgConstants.DarkPausedColor,
        _ => stroke
    };

    private static string? BuildBadgeSvg(UpsOperationalState state, string stroke, string halo, string fill) => state switch
    {
        UpsOperationalState.LineNormal => string.Format(
            CultureInfo.InvariantCulture,
            FluentBatterySvgConstants.ChargingBoltBadgeFormat,
            stroke,
            fill),
        UpsOperationalState.ChargingAvr => string.Format(
            CultureInfo.InvariantCulture,
            FluentBatterySvgConstants.AvrWaveBadgeFormat,
            stroke,
            halo),
        UpsOperationalState.Fault => string.Format(
            CultureInfo.InvariantCulture,
            FluentBatterySvgConstants.FaultTriangleBadgeFormat,
            stroke,
            halo),
        UpsOperationalState.Paused => string.Format(
            CultureInfo.InvariantCulture,
            FluentBatterySvgConstants.PausedBarsBadgeFormat,
            stroke,
            halo),
        UpsOperationalState.CommunicationsLost => string.Format(
            CultureInfo.InvariantCulture,
            FluentBatterySvgConstants.NoCommCrossBadgeFormat,
            stroke),
        _ => null
    };
}
