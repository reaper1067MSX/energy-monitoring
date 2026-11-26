namespace VoltTray.Core.Battery;

public readonly record struct BatteryVoltageBand(int SeriesCount, double MinVoltageDc, double MaxVoltageDc);
