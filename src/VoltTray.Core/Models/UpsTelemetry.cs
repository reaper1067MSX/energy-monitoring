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
    DateTimeOffset Timestamp)
{
    public static UpsTelemetry CreateDefault(DateTimeOffset? timestamp = null) =>
        new(
            InputVoltageAc: 116.2,
            FaultReferenceVoltageAc: 116.2,
            OutputVoltageAc: 116.2,
            LoadPercentage: 14,
            ActiveLoadWatts: 126,
            FrequencyHz: 60.0,
            BatteryVoltageDc: 27.41,
            Cell1VoltageDc: 13.70,
            Cell2VoltageDc: 13.70,
            BatteryPercentage: 100,
            EstimatedRuntimeMinutes: 25,
            TemperatureCelsius: 25.0,
            State: UpsOperationalState.LineNormal,
            IsBeeperMuted: false,
            IsTestInProgress: false,
            BankSpec: new BatteryBankSpec(),
            Timestamp: timestamp ?? DateTimeOffset.UtcNow);
}
