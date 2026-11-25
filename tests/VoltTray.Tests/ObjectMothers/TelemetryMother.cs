using VoltTray.Core.Models;

namespace VoltTray.Tests.ObjectMothers;

/// <summary>
/// Object Mother providing pre-configured domain UpsTelemetry fixtures
/// representing all operational states and typical boundary conditions.
/// </summary>
public static class TelemetryMother
{
    public const double DefaultNominalFrequencyHz = 60.0;
    public const double DefaultAmbientTemperatureCelsius = 25.0;
    public static UpsTelemetry CreateLineNormal(
        double inV = 117.3,
        double outV = 117.3,
        int batteryPct = 100,
        int loadPct = 14,
        int activeWatts = 126,
        int runtimeMinutes = 32,
        double batV = 27.41,
        BatteryBankSpec? bankSpec = null,
        DateTimeOffset? timestamp = null)
    {
        return new UpsTelemetry(
            InputVoltageAc: inV,
            FaultReferenceVoltageAc: inV,
            OutputVoltageAc: outV,
            LoadPercentage: loadPct,
            ActiveLoadWatts: activeWatts,
            FrequencyHz: DefaultNominalFrequencyHz,
            BatteryVoltageDc: batV,
            Cell1VoltageDc: Math.Round(batV / 2.0, 2),
            Cell2VoltageDc: Math.Round(batV / 2.0, 2),
            BatteryPercentage: batteryPct,
            EstimatedRuntimeMinutes: runtimeMinutes,
            TemperatureCelsius: DefaultAmbientTemperatureCelsius,
            State: UpsOperationalState.LineNormal,
            IsBeeperMuted: true,
            IsTestInProgress: false,
            BankSpec: bankSpec ?? new BatteryBankSpec(),
            Timestamp: timestamp ?? DateTimeOffset.UtcNow);
    }

    public static UpsTelemetry CreateOnBattery(
        int batteryPct = 73,
        int loadPct = 25,
        int activeWatts = 225,
        int runtimeMinutes = 18,
        double batV = 24.8,
        BatteryBankSpec? bankSpec = null,
        DateTimeOffset? timestamp = null)
    {
        return new UpsTelemetry(
            InputVoltageAc: 0.0,
            FaultReferenceVoltageAc: 0.0,
            OutputVoltageAc: 119.8,
            LoadPercentage: loadPct,
            ActiveLoadWatts: activeWatts,
            FrequencyHz: DefaultNominalFrequencyHz,
            BatteryVoltageDc: batV,
            Cell1VoltageDc: Math.Round(batV / 2.0, 2),
            Cell2VoltageDc: Math.Round(batV / 2.0, 2),
            BatteryPercentage: batteryPct,
            EstimatedRuntimeMinutes: runtimeMinutes,
            TemperatureCelsius: DefaultAmbientTemperatureCelsius,
            State: UpsOperationalState.OnBattery,
            IsBeeperMuted: false,
            IsTestInProgress: false,
            BankSpec: bankSpec ?? new BatteryBankSpec(),
            Timestamp: timestamp ?? DateTimeOffset.UtcNow);
    }

    public static UpsTelemetry CreateBatteryLow(
        int batteryPct = 15,
        int loadPct = 30,
        int activeWatts = 270,
        int runtimeMinutes = 3,
        double batV = 21.6,
        BatteryBankSpec? bankSpec = null,
        DateTimeOffset? timestamp = null)
    {
        return new UpsTelemetry(
            InputVoltageAc: 0.0,
            FaultReferenceVoltageAc: 0.0,
            OutputVoltageAc: 118.5,
            LoadPercentage: loadPct,
            ActiveLoadWatts: activeWatts,
            FrequencyHz: DefaultNominalFrequencyHz,
            BatteryVoltageDc: batV,
            Cell1VoltageDc: Math.Round(batV / 2.0, 2),
            Cell2VoltageDc: Math.Round(batV / 2.0, 2),
            BatteryPercentage: batteryPct,
            EstimatedRuntimeMinutes: runtimeMinutes,
            TemperatureCelsius: DefaultAmbientTemperatureCelsius,
            State: UpsOperationalState.BatteryLow,
            IsBeeperMuted: false,
            IsTestInProgress: false,
            BankSpec: bankSpec ?? new BatteryBankSpec(),
            Timestamp: timestamp ?? DateTimeOffset.UtcNow);
    }

    public static UpsTelemetry CreateChargingAvr(
        double inV = 98.0,
        double outV = 118.0,
        int batteryPct = 90,
        int loadPct = 15,
        BatteryBankSpec? bankSpec = null,
        DateTimeOffset? timestamp = null)
    {
        return new UpsTelemetry(
            InputVoltageAc: inV,
            FaultReferenceVoltageAc: inV,
            OutputVoltageAc: outV,
            LoadPercentage: loadPct,
            ActiveLoadWatts: (int)Math.Round(loadPct * 9.0),
            FrequencyHz: DefaultNominalFrequencyHz,
            BatteryVoltageDc: 27.2,
            Cell1VoltageDc: 13.6,
            Cell2VoltageDc: 13.6,
            BatteryPercentage: batteryPct,
            EstimatedRuntimeMinutes: 30,
            TemperatureCelsius: DefaultAmbientTemperatureCelsius,
            State: UpsOperationalState.ChargingAvr,
            IsBeeperMuted: true,
            IsTestInProgress: false,
            BankSpec: bankSpec ?? new BatteryBankSpec(),
            Timestamp: timestamp ?? DateTimeOffset.UtcNow);
    }

    public static UpsTelemetry CreateFault(
        BatteryBankSpec? bankSpec = null,
        DateTimeOffset? timestamp = null)
    {
        return new UpsTelemetry(
            InputVoltageAc: 0.0,
            FaultReferenceVoltageAc: 0.0,
            OutputVoltageAc: 0.0,
            LoadPercentage: 0,
            ActiveLoadWatts: 0,
            FrequencyHz: 0.0,
            BatteryVoltageDc: 24.0,
            Cell1VoltageDc: 12.0,
            Cell2VoltageDc: 12.0,
            BatteryPercentage: 0,
            EstimatedRuntimeMinutes: 0,
            TemperatureCelsius: 0.0,
            State: UpsOperationalState.Fault,
            IsBeeperMuted: false,
            IsTestInProgress: false,
            BankSpec: bankSpec ?? new BatteryBankSpec(),
            Timestamp: timestamp ?? DateTimeOffset.UtcNow);
    }

    public static UpsTelemetry CreateSelfTest(
        int batteryPct = 98,
        BatteryBankSpec? bankSpec = null,
        DateTimeOffset? timestamp = null)
    {
        return new UpsTelemetry(
            InputVoltageAc: 117.3,
            FaultReferenceVoltageAc: 117.3,
            OutputVoltageAc: 117.3,
            LoadPercentage: 14,
            ActiveLoadWatts: 126,
            FrequencyHz: DefaultNominalFrequencyHz,
            BatteryVoltageDc: 26.94,
            Cell1VoltageDc: 13.47,
            Cell2VoltageDc: 13.47,
            BatteryPercentage: batteryPct,
            EstimatedRuntimeMinutes: 31,
            TemperatureCelsius: DefaultAmbientTemperatureCelsius,
            State: UpsOperationalState.LineNormal,
            IsBeeperMuted: true,
            IsTestInProgress: true,
            BankSpec: bankSpec ?? new BatteryBankSpec(),
            Timestamp: timestamp ?? DateTimeOffset.UtcNow);
    }

    public static UpsTelemetry CreatePaused(
        BatteryBankSpec? bankSpec = null,
        DateTimeOffset? timestamp = null)
    {
        return new UpsTelemetry(
            InputVoltageAc: 0.0,
            FaultReferenceVoltageAc: 0.0,
            OutputVoltageAc: 0.0,
            LoadPercentage: 0,
            ActiveLoadWatts: 0,
            FrequencyHz: 0.0,
            BatteryVoltageDc: 0.0,
            Cell1VoltageDc: 0.0,
            Cell2VoltageDc: 0.0,
            BatteryPercentage: 0,
            EstimatedRuntimeMinutes: 0,
            TemperatureCelsius: 0.0,
            State: UpsOperationalState.Paused,
            IsBeeperMuted: true,
            IsTestInProgress: false,
            BankSpec: bankSpec ?? new BatteryBankSpec(),
            Timestamp: timestamp ?? DateTimeOffset.UtcNow);
    }
}
