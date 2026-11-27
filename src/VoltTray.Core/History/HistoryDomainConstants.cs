namespace VoltTray.Core.History;

public static class HistoryDomainConstants
{
    public const double MaxIntegrationStepSeconds = 30.0;
    public const int DefaultFlushIntervalSeconds = 60;
    public const int DefaultRecentDaysWindow = 30;
    public const int DefaultMaxOutageEvents = 20;
    public const double SecondsPerHour = 3600.0;
    public const double WattsPerKilowatt = 1000.0;
    public const double ValidMainsVoltageFloorAc = 10.0;
    public const double FloatVoltagePerBatteryThreshold = 13.55;
}
