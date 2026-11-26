namespace VoltTray.Core.Models;

public readonly record struct BatteryVoltageBand(int SeriesCount, double MinVoltageDc, double MaxVoltageDc);
