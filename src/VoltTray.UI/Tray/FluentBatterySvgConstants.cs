namespace VoltTray.UI.Tray;

internal static class FluentBatterySvgConstants
{
    public const int ViewBoxWidth = 28;
    public const int ViewBoxHeight = 16;

    public const double MaxFillBarWidth = 17.6;
    public const double MinFillBarWidth = 1.8;

    public const string LightStrokeColor = "#18181b";
    public const string DarkStrokeColor = "#f4f4f5";
    public const string LightHaloColor = "#f3f4f6";
    public const string DarkHaloColor = "#18181b";

    public const string LightOnlineGreenColor = "#16a34a";
    public const string DarkOnlineGreenColor = "#4ade80";
    public const string LightOnBatteryColor = "#d97706";
    public const string DarkOnBatteryColor = "#fbbf24";
    public const string LightCriticalColor = "#dc2626";
    public const string DarkCriticalColor = "#f87171";
    public const string LightAvrColor = "#2563eb";
    public const string DarkAvrColor = "#60a5fa";
    public const string LightPausedColor = "#64748b";
    public const string DarkPausedColor = "#94a3b8";

    public const string ShellElementFormat =
        "  <rect x=\"1.5\" y=\"2.2\" width=\"22\" height=\"11.6\" rx=\"2.8\" stroke=\"{0}\" stroke-width=\"1.5\" fill=\"none\" />\n" +
        "  <path d=\"M25.6 5.6V10.4\" stroke=\"{0}\" stroke-width=\"1.8\" stroke-linecap=\"round\" />";

    public const string FillBarFormat =
        "  <rect x=\"3.7\" y=\"4.4\" width=\"{0}\" height=\"7.2\" rx=\"1.2\" fill=\"{1}\" />";

    public const string OnlinePlugBadgeFormat =
        "  <g stroke=\"{1}\" stroke-width=\"2.4\" stroke-linecap=\"round\" stroke-linejoin=\"round\" fill=\"{1}\">\n" +
        "    <path d=\"M11 1.4V3.8M14 1.4V3.8\" />\n" +
        "    <path d=\"M9.6 4H15.4V6.4C15.4 7.9 14.2 9.1 12.7 9.1H12.3C10.8 9.1 9.6 7.9 9.6 6.4V4Z\" />\n" +
        "    <path d=\"M12.5 9.1V11.2\" />\n" +
        "  </g>\n" +
        "  <g stroke=\"{0}\" stroke-width=\"1.35\" stroke-linecap=\"round\" stroke-linejoin=\"round\" fill=\"{0}\">\n" +
        "    <path d=\"M11 1.4V3.8M14 1.4V3.8\" fill=\"none\" />\n" +
        "    <path d=\"M9.7 4.1H15.3V6.3C15.3 7.7 14.1 8.9 12.7 8.9H12.3C10.9 8.9 9.7 7.7 9.7 6.3V4.1Z\" stroke=\"none\" />\n" +
        "    <path d=\"M12.5 8.8V11.2\" fill=\"none\" />\n" +
        "  </g>";

    public const string ChargingBoltBadgeFormat =
        "  <path d=\"M13.4 1.2L8.9 8.2H12.4L11.4 14L16.1 6.8H12.6L13.4 1.2Z\" stroke=\"{1}\" stroke-width=\"2.2\" stroke-linejoin=\"round\" fill=\"{1}\" />\n" +
        "  <path d=\"M13.4 1.2L8.9 8.2H12.4L11.4 14L16.1 6.8H12.6L13.4 1.2Z\" fill=\"{0}\" />";

    public const string AvrWaveBadgeFormat =
        "  <path d=\"M8.2 8.2C9.2 5.4 10.4 5.4 11.4 8.2C12.4 11 13.6 11 14.6 8.2C15.2 6.6 16 6.2 16.8 7\" stroke=\"{1}\" stroke-width=\"3.2\" stroke-linecap=\"round\" fill=\"none\" />\n" +
        "  <path d=\"M8.2 8.2C9.2 5.4 10.4 5.4 11.4 8.2C12.4 11 13.6 11 14.6 8.2C15.2 6.6 16 6.2 16.8 7\" stroke=\"{0}\" stroke-width=\"1.6\" stroke-linecap=\"round\" fill=\"none\" />";

    public const string FaultTriangleBadgeFormat =
        "  <path d=\"M12.5 3.6L17.2 12.4H7.8L12.5 3.6Z\" stroke=\"{1}\" stroke-width=\"2.2\" stroke-linejoin=\"round\" fill=\"{1}\" />\n" +
        "  <path d=\"M12.5 3.6L17.2 12.4H7.8L12.5 3.6Z\" fill=\"{0}\" />\n" +
        "  <path d=\"M12.5 6.8V9.4\" stroke=\"{1}\" stroke-width=\"1.3\" stroke-linecap=\"round\" />\n" +
        "  <circle cx=\"12.5\" cy=\"11\" r=\"0.7\" fill=\"{1}\" />";

    public const string PausedBarsBadgeFormat =
        "  <g stroke=\"{1}\" stroke-width=\"2.2\" stroke-linecap=\"round\">\n" +
        "    <path d=\"M10.8 5.2V10.8M14.2 5.2V10.8\" />\n" +
        "  </g>\n" +
        "  <g stroke=\"{0}\" stroke-width=\"1.6\" stroke-linecap=\"round\">\n" +
        "    <path d=\"M10.8 5.2V10.8M14.2 5.2V10.8\" />\n" +
        "  </g>";

    public const string NoCommCrossBadgeFormat =
        "  <path d=\"M10.2 5.7L14.8 10.3M14.8 5.7L10.2 10.3\" stroke=\"{0}\" stroke-width=\"1.5\" stroke-linecap=\"round\" />";
}
