using VoltTray.Core.Telemetry;

namespace VoltTray.Core.Battery;

public interface IBatteryAutonomyEstimator
{
    int? EstimateRuntimeMinutes(
        int loadPercentage,
        UpsCapabilities capabilities,
        double capacityFactor = 1.0,
        bool isHardwareLow = false);

    int CalculateSoCPercentage(double batteryVoltageDc, int seriesBatteryCount = 2);
}
