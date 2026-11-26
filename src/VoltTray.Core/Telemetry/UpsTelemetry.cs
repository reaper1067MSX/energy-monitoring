using VoltTray.Core.Battery;
namespace VoltTray.Core.Telemetry;

public sealed record UpsTelemetry(
    double InputVoltageAc,
    double? FaultReferenceVoltageAc,
    double OutputVoltageAc,
    int LoadPercentage,
    int? ActiveLoadWatts,
    double FrequencyHz,
    double BatteryVoltageDc,
    double Cell1VoltageDc,
    double Cell2VoltageDc,
    int BatteryPercentage,
    int? EstimatedRuntimeMinutes,
    double? TemperatureCelsius,
    UpsOperationalState State,
    bool IsBeeperMuted,
    bool IsTestInProgress,
    BatteryBankSpec BankSpec,
    DateTimeOffset Timestamp,
    PowerStatus? PowerStatus = null)
{
    public static UpsTelemetry CreateDefault(DateTimeOffset? timestamp = null)
    {
        return new UpsTelemetry(
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
            TemperatureCelsius: null,
            State: UpsOperationalState.LineNormal,
            IsBeeperMuted: false,
            IsTestInProgress: false,
            BankSpec: new BatteryBankSpec(),
            Timestamp: timestamp ?? DateTimeOffset.UtcNow,
            PowerStatus: new PowerStatus(
                IsOnBattery: false,
                IsBatteryLow: false,
                IsAvrActive: false,
                IsFault: false,
                IsLineInteractive: true,
                IsSelfTestActive: false,
                IsShutdownActive: false,
                IsBeeperActive: true));
    }
}
