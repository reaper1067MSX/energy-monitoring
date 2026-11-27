using VoltTray.Core.Battery;

namespace VoltTray.Core.Telemetry;

public static class UpsModelCatalog
{
    private static readonly UpsModelProfile Nt2401ProProfile = new(
        Preset: UpsModelPreset.ForzaNt2401Pro,
        CliKey: UpsModelConstants.Nt2401ProCliKey,
        DisplayName: UpsModelConstants.Nt2401ProDisplayName,
        RatedVa: UpsModelConstants.Nt2401ProRatedVa,
        RatedWatts: UpsModelConstants.ForzaDualBatteryRatedWatts,
        BankSpec: new BatteryBankSpec(
            SeriesBatteryCount: UpsModelConstants.DefaultSeriesBatteryCount,
            NominalVoltageDc: UpsModelConstants.DefaultNominalVoltageDc,
            RatedCapacityAh: UpsModelConstants.DefaultRatedCapacityAh,
            Label: UpsModelConstants.Nt2401ProDisplayName));

    private static readonly UpsModelProfile Fx2200LcdProfile = new(
        Preset: UpsModelPreset.ForzaFx2200Lcd,
        CliKey: UpsModelConstants.Fx2200LcdCliKey,
        DisplayName: UpsModelConstants.Fx2200LcdDisplayName,
        RatedVa: UpsModelConstants.Fx2200LcdRatedVa,
        RatedWatts: UpsModelConstants.ForzaDualBatteryRatedWatts,
        BankSpec: new BatteryBankSpec(
            SeriesBatteryCount: UpsModelConstants.DefaultSeriesBatteryCount,
            NominalVoltageDc: UpsModelConstants.DefaultNominalVoltageDc,
            RatedCapacityAh: UpsModelConstants.DefaultRatedCapacityAh,
            Label: UpsModelConstants.Fx2200LcdDisplayName));

    public static UpsModelProfile GetProfile(UpsModelPreset preset) => preset switch
    {
        UpsModelPreset.ForzaNt2401Pro => Nt2401ProProfile,
        UpsModelPreset.ForzaFx2200Lcd => Fx2200LcdProfile,
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "Unsupported UPS model preset.")
    };

    public static bool TryParseCliKey(string? value, out UpsModelPreset preset)
    {
        if (string.Equals(value, UpsModelConstants.Nt2401ProCliKey, StringComparison.OrdinalIgnoreCase))
        {
            preset = UpsModelPreset.ForzaNt2401Pro;
            return true;
        }

        if (string.Equals(value, UpsModelConstants.Fx2200LcdCliKey, StringComparison.OrdinalIgnoreCase))
        {
            preset = UpsModelPreset.ForzaFx2200Lcd;
            return true;
        }

        preset = default;
        return false;
    }
}
