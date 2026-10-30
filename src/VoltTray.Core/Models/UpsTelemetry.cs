namespace VoltTray.Core.Models;

public sealed record UpsTelemetry(
    double InputVoltageAc,
    double FaultReferenceVoltageAc,
    double OutputVoltageAc,
    int LoadPercentage,
    int ActiveLoadWatts,
    double FrequencyHz,
    double BatteryVoltageDc,
    double Cell1VoltageDc,
    double Cell2VoltageDc,
    int BatteryPercentage,
    int EstimatedRuntimeMinutes,
    double TemperatureCelsius,
    UpsOperationalState State,
    bool IsBeeperMuted,
    bool IsTestInProgress,
    BatteryBankSpec BankSpec,
    DateTimeOffset Timestamp);
