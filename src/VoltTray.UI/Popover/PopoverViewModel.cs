using System.Globalization;
using VoltTray.Core.Telemetry;
using VoltTray.Shared;
using VoltTray.UI.Tokens;

namespace VoltTray.UI.Popover;

/// <summary>
/// Framework-agnostic presentation model for the Option C Popover card.
/// Derives dynamic device title/emblem and omits Unavailable (null) or duplicate metrics.
/// </summary>
public sealed record PopoverViewModel(
    string Title,
    string Emblem,
    string BankSubtitle,
    string StatusLabel,
    UpsOperationalState State,
    string InputVoltageText,
    string OutputVoltageText,
    string FrequencyText,
    string? FaultReferenceText,
    string? TemperatureText,
    string BatteryVoltageText,
    string BatteryPercentageText,
    string RuntimeText,
    string LoadPercentageText,
    string? ActiveLoadWattsText,
    int LoadPercentage,
    string SelfTestButtonLabel,
    string BeeperButtonLabel,
    bool IsBeeperMuted,
    bool IsTestInProgress,
    string RegulationModeText = LocalizationConstants.UnavailablePlaceholder,
    string? CellsSummaryText = null,
    string? BankEnergyWhText = null,
    string? Cell1Text = null,
    string? Cell2Text = null,
    string? ActiveLoadCeilingText = null)
{
    public static PopoverViewModel FromTelemetry(UpsTelemetry telemetry, CultureInfo? culture = null)
    {
        CultureInfo resolvedCulture = culture ?? CultureInfo.CurrentUICulture;
        bool isSpanish = resolvedCulture.TwoLetterISOLanguageName
            .Equals(LocalizationConstants.SpanishCulturePrefix, StringComparison.OrdinalIgnoreCase);

        string title = string.IsNullOrWhiteSpace(telemetry.BankSpec.Label)
            ? ApplicationDefaults.DefaultUpsDisplayName
            : telemetry.BankSpec.Label.Trim();

        string emblem = DeriveEmblem(title);

        string bankSubtitle = telemetry.BankSpec.RatedCapacityAh.HasValue
            ? FormattableString.Invariant(
                $"{telemetry.BankSpec.SeriesBatteryCount}× 12V · {telemetry.BankSpec.NominalVoltageDc:0}V · {telemetry.BankSpec.RatedCapacityAh.Value:0.#}Ah")
            : FormattableString.Invariant(
                $"{telemetry.BankSpec.SeriesBatteryCount}× 12V · {telemetry.BankSpec.NominalVoltageDc:0}V");

        string statusLabel = FormatStatusLabel(telemetry.State, isSpanish);
        string runtimeText = RuntimeDisplayFormatter.FormatRuntimeString(telemetry.EstimatedRuntimeMinutes)
            ?? LocalizationConstants.UnavailablePlaceholder;

        bool hasDistinctFaultRef = telemetry.FaultReferenceVoltageAc.HasValue &&
                                   Math.Abs(telemetry.FaultReferenceVoltageAc.Value - telemetry.InputVoltageAc) >= 0.05;
        string? faultRefText = hasDistinctFaultRef
            ? FormattableString.Invariant($"{telemetry.FaultReferenceVoltageAc!.Value:F1} V")
            : null;
        string? temperatureText = telemetry.TemperatureCelsius.HasValue
            ? FormattableString.Invariant($"{telemetry.TemperatureCelsius.Value:F1} °C")
            : null;
        string? wattsText = telemetry.ActiveLoadWatts.HasValue
            ? FormattableString.Invariant($"{telemetry.ActiveLoadWatts.Value} W")
            : null;
        string? activeLoadCeilingText = null;
        if (telemetry.ActiveLoadWatts is int activeWatts)
        {
            if (telemetry.LoadPercentage > 0)
            {
                int inferredRated = (int)(Math.Round((activeWatts * 100.0 / telemetry.LoadPercentage) / 50.0) * 50);
                string prep = isSpanish ? LocalizationConstants.LoadOfSeparatorEs : LocalizationConstants.LoadOfSeparatorEn;
                activeLoadCeilingText = FormattableString.Invariant($"({activeWatts} W {prep} {inferredRated}W)");
            }
            else
            {
                activeLoadCeilingText = FormattableString.Invariant($"({activeWatts} W)");
            }
        }

        string regulationModeText = FormatRegulationMode(telemetry.State, isSpanish);
        string? cellsSummaryText = telemetry.Cell1VoltageDc > 0 && telemetry.Cell2VoltageDc > 0
            ? FormattableString.Invariant($"C1 {telemetry.Cell1VoltageDc:F2}V · C2 {telemetry.Cell2VoltageDc:F2}V")
            : null;
        string? cell1Text = telemetry.Cell1VoltageDc > 0
            ? FormattableString.Invariant($"{telemetry.Cell1VoltageDc:F2} V")
            : null;
        string? cell2Text = telemetry.Cell2VoltageDc > 0
            ? FormattableString.Invariant($"{telemetry.Cell2VoltageDc:F2} V")
            : null;
        string? bankEnergyWhText = telemetry.BankSpec.RatedCapacityAh is double ratedAh && ratedAh > 0
            ? FormattableString.Invariant($"{(int)Math.Round(telemetry.BankSpec.NominalVoltageDc * ratedAh)} Wh")
            : null;

        string selfTestLabel = telemetry.IsTestInProgress
            ? (isSpanish ? LocalizationConstants.BatteryTestRunningEs : LocalizationConstants.BatteryTestRunningEn)
            : (isSpanish ? LocalizationConstants.BatteryTestEs : LocalizationConstants.BatteryTestEn);

        string beeperLabel = telemetry.IsBeeperMuted
            ? (isSpanish ? LocalizationConstants.UnmuteAlarmEs : LocalizationConstants.UnmuteAlarmEn)
            : (isSpanish ? LocalizationConstants.MuteAlarmEs : LocalizationConstants.MuteAlarmEn);

        return new PopoverViewModel(
            Title: title,
            Emblem: emblem,
            BankSubtitle: bankSubtitle,
            StatusLabel: statusLabel,
            State: telemetry.State,
            InputVoltageText: FormattableString.Invariant($"{telemetry.InputVoltageAc:F1} V AC"),
            OutputVoltageText: FormattableString.Invariant($"{telemetry.OutputVoltageAc:F1} V"),
            FrequencyText: FormattableString.Invariant($"{telemetry.FrequencyHz:F1} Hz"),
            FaultReferenceText: faultRefText,
            TemperatureText: temperatureText,
            BatteryVoltageText: FormattableString.Invariant($"{telemetry.BatteryVoltageDc:F2} V"),
            BatteryPercentageText: FormattableString.Invariant($"{telemetry.BatteryPercentage}%"),
            RuntimeText: runtimeText,
            LoadPercentageText: FormattableString.Invariant($"{telemetry.LoadPercentage}%"),
            ActiveLoadWattsText: wattsText,
            LoadPercentage: Math.Clamp(telemetry.LoadPercentage, 0, 100),
            SelfTestButtonLabel: selfTestLabel,
            BeeperButtonLabel: beeperLabel,
            IsBeeperMuted: telemetry.IsBeeperMuted,
            IsTestInProgress: telemetry.IsTestInProgress,
            RegulationModeText: regulationModeText,
            CellsSummaryText: cellsSummaryText,
            BankEnergyWhText: bankEnergyWhText,
            Cell1Text: cell1Text,
            Cell2Text: cell2Text,
            ActiveLoadCeilingText: activeLoadCeilingText);
    }

    private static string DeriveEmblem(string title)
    {
        string[] words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length >= 2)
        {
            return string.Concat(char.ToUpperInvariant(words[0][0]), char.ToUpperInvariant(words[1][0]));
        }

        return title.Length >= 2
            ? title[..2].ToUpperInvariant()
            : title.ToUpperInvariant();
    }

    private static string FormatStatusLabel(UpsOperationalState state, bool isSpanish) => state switch
    {
        UpsOperationalState.LineNormal => isSpanish ? LocalizationConstants.StatusOnlineEs : LocalizationConstants.StatusOnlineEn,
        UpsOperationalState.OnBattery => isSpanish ? LocalizationConstants.StatusOnBatteryEs : LocalizationConstants.StatusOnBatteryEn,
        UpsOperationalState.BatteryLow => isSpanish ? LocalizationConstants.StatusOnBatteryEs : LocalizationConstants.StatusOnBatteryEn,
        UpsOperationalState.ChargingAvr => isSpanish ? LocalizationConstants.StatusAvrEs : LocalizationConstants.StatusAvrEn,
        UpsOperationalState.Fault => isSpanish ? LocalizationConstants.StatusFaultEs : LocalizationConstants.StatusFaultEn,
        UpsOperationalState.Paused => isSpanish ? LocalizationConstants.StatusPausedEs : LocalizationConstants.StatusPausedEn,
        _ => isSpanish ? LocalizationConstants.StatusNoCommEs : LocalizationConstants.StatusNoCommEn
    };

    private static string FormatRegulationMode(UpsOperationalState state, bool isSpanish) => state switch
    {
        UpsOperationalState.ChargingAvr => isSpanish ? LocalizationConstants.ModeAvrEs : LocalizationConstants.ModeAvrEn,
        UpsOperationalState.LineNormal or UpsOperationalState.Paused => isSpanish
            ? LocalizationConstants.ModeLineInteractiveEs
            : LocalizationConstants.ModeLineInteractiveEn,
        UpsOperationalState.OnBattery or UpsOperationalState.BatteryLow => isSpanish
            ? LocalizationConstants.ModeInverterEs
            : LocalizationConstants.ModeInverterEn,
        _ => LocalizationConstants.UnavailablePlaceholder
    };
}
