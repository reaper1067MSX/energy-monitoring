namespace VoltTray.Core.Ports;

public interface IBatteryAutonomyEstimator
{
    int EstimateRuntimeMinutes(int loadPercentage, double capacityFactor = 1.0, bool isHardwareLow = false);
    int CalculateSoCPercentage(double batteryVoltageDc);
    string FormatRuntimeString(int minutes, string cultureName = "en");
    string FormatRuntimeCaption(int loadPercentage, string cultureName = "en");
}
