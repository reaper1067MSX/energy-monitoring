namespace VoltTray.UI.Tray;

internal static class RingBatterySvgConstants
{
    public const int ViewBoxSize = 24;
    public const double RingRadius = 9.2;
    public const double RingCircumference = 57.81;

    public const string LightTrackColor = "#d4d4d8";
    public const string DarkTrackColor = "#3f3f46";
    public const string LightStrokeColor = "#18181b";
    public const string DarkStrokeColor = "#f4f4f5";
    public const string LightHaloColor = "#f3f4f6";
    public const string DarkHaloColor = "#18181b";

    public const string LightOnlineColor = "#16a34a";
    public const string DarkOnlineColor = "#4ade80";
    public const string LightOnBatteryColor = "#d97706";
    public const string DarkOnBatteryColor = "#fbbf24";
    public const string LightCriticalColor = "#dc2626";
    public const string DarkCriticalColor = "#f87171";
    public const string LightAvrColor = "#2563eb";
    public const string DarkAvrColor = "#60a5fa";
    public const string LightPausedColor = "#64748b";
    public const string DarkPausedColor = "#94a3b8";

    public const string TrackCircleFormat =
        "  <circle cx=\"12\" cy=\"12\" r=\"9.2\" stroke=\"{0}\" stroke-width=\"2.4\" opacity=\"0.45\" />";

    public const string ProgressCircleFormat =
        "  <circle cx=\"12\" cy=\"12\" r=\"9.2\" stroke=\"{0}\" stroke-width=\"2.4\" stroke-linecap=\"round\" stroke-dasharray=\"57.81\" stroke-dashoffset=\"{1}\" transform=\"rotate(-90 12 12)\" />";

    public const string OnlineBoltGlyphFormat =
        "  <path d=\"M12.8 6.8L8.8 12.6H11.8L11.2 17.2L15.2 11.4H12.2L12.8 6.8Z\" fill=\"{0}\" />";

    public const string OnBatteryGlyphFormat =
        "  <rect x=\"7.8\" y=\"9\" width=\"7.4\" height=\"6\" rx=\"1.2\" stroke=\"{0}\" stroke-width=\"1.4\" fill=\"none\" />\n" +
        "  <path d=\"M16.4 10.8V13.2\" stroke=\"{0}\" stroke-width=\"1.4\" stroke-linecap=\"round\" />";

    public const string LowBatteryGlyphFormat =
        "  <path d=\"M12 7.8V12.8\" stroke=\"{0}\" stroke-width=\"2\" stroke-linecap=\"round\" />\n" +
        "  <circle cx=\"12\" cy=\"15.8\" r=\"1.15\" fill=\"{0}\" />";

    public const string AvrWaveGlyphFormat =
        "  <path d=\"M7.6 12.2C8.7 9.2 10 9.2 11.2 12.2C12.4 15.2 13.7 15.2 14.9 12.2C15.5 10.8 16 10.3 16.5 10.8\" stroke=\"{0}\" stroke-width=\"1.8\" stroke-linecap=\"round\" fill=\"none\" />";

    public const string FaultTriangleGlyphFormat =
        "  <path d=\"M12 7.2L16.5 15.4H7.5L12 7.2Z\" fill=\"{0}\" />\n" +
        "  <path d=\"M12 10.1V12.5\" stroke=\"{1}\" stroke-width=\"1.3\" stroke-linecap=\"round\" />\n" +
        "  <circle cx=\"12\" cy=\"14\" r=\"0.7\" fill=\"{1}\" />";

    public const string PausedBarsGlyphFormat =
        "  <path d=\"M10.2 8.8V15.2M13.8 8.8V15.2\" stroke=\"{0}\" stroke-width=\"1.8\" stroke-linecap=\"round\" />";

    public const string NoCommCrossGlyphFormat =
        "  <path d=\"M9 9L15 15M15 9L9 15\" stroke=\"{0}\" stroke-width=\"1.8\" stroke-linecap=\"round\" />";
}
