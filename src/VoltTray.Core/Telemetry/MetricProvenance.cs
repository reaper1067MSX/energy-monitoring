namespace VoltTray.Core.Telemetry;

public enum MetricProvenance
{
    HardwareMeasured = 0,
    DerivedFromMeasured = 1,
    ConfiguredDerived = 2,
    Unavailable = 3
}
