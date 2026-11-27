using System.Globalization;
using VoltTray.Core.Telemetry;
using VoltTray.Hosting;
using VoltTray.Infrastructure.Settings;
using VoltTray.UI.Popover;
using Xunit;

namespace VoltTray.Tests;

public class UpsModelSelectorTests
{
    [Fact]
    public void UpsModelCatalog_ExposesExactForzaNt2401ProAndFx2200LcdSpecs()
    {
        UpsModelProfile nt2401 = UpsModelCatalog.GetProfile(UpsModelPreset.ForzaNt2401Pro);
        UpsModelProfile fx2200 = UpsModelCatalog.GetProfile(UpsModelPreset.ForzaFx2200Lcd);

        Assert.Equal("Forza NT-2401PRO", nt2401.DisplayName);
        Assert.Equal(2400, nt2401.RatedVa);
        Assert.Equal(1200, nt2401.RatedWatts);
        Assert.Equal(2, nt2401.BankSpec.SeriesBatteryCount);
        Assert.Equal(24.0, nt2401.BankSpec.NominalVoltageDc);
        Assert.Equal(9.0, nt2401.BankSpec.RatedCapacityAh);

        Assert.Equal("Forza FX-2200 LCD", fx2200.DisplayName);
        Assert.Equal(2200, fx2200.RatedVa);
        Assert.Equal(1200, fx2200.RatedWatts);

        Assert.True(UpsModelCatalog.TryParseCliKey("NT2401PRO", out UpsModelPreset parsedNt));
        Assert.Equal(UpsModelPreset.ForzaNt2401Pro, parsedNt);
        Assert.True(UpsModelCatalog.TryParseCliKey("fx2200", out UpsModelPreset parsedFx));
        Assert.Equal(UpsModelPreset.ForzaFx2200Lcd, parsedFx);
        Assert.False(UpsModelCatalog.TryParseCliKey("apc-1500", out _));
    }

    [Fact]
    public void JsonUserSettingsStore_RoundTripsSelectedModel_AndHandlesMissingOrCorruptFiles()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"volttray-settings-{Guid.NewGuid():N}");
        string settingsPath = Path.Combine(tempDirectory, "settings.json");

        try
        {
            var store = new JsonUserSettingsStore(settingsPath);
            Assert.Null(store.LoadSelectedModel());

            store.SaveSelectedModel(UpsModelPreset.ForzaFx2200Lcd);
            Assert.Equal(UpsModelPreset.ForzaFx2200Lcd, store.LoadSelectedModel());

            store.SaveSelectedModel(UpsModelPreset.ForzaNt2401Pro);
            Assert.Equal(UpsModelPreset.ForzaNt2401Pro, store.LoadSelectedModel());

            store.SaveIconMode(TrayIconMode.Short);
            Assert.Equal(TrayIconMode.Short, store.LoadIconMode());
            Assert.Equal(UpsModelPreset.ForzaNt2401Pro, store.LoadSelectedModel());

            store.SaveIconMode(TrayIconMode.Large);
            Assert.Equal(TrayIconMode.Large, store.LoadIconMode());

            File.WriteAllText(settingsPath, "{ not valid json");
            Assert.Null(store.LoadSelectedModel());
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void ApplicationOptionsParser_ParsesValidModels_AndRejectsUnknownModels()
    {
        TrayMonitorLaunchOptions ntOptions = Assert.IsType<TrayMonitorLaunchOptions>(
            ApplicationOptionsParser.Parse(["--model", "nt2401pro"]));
        Assert.Equal(UpsModelPreset.ForzaNt2401Pro, ntOptions.SelectedModel);

        TrayMonitorLaunchOptions fxOptions = Assert.IsType<TrayMonitorLaunchOptions>(
            ApplicationOptionsParser.Parse(["--model", "fx2200"]));
        Assert.Equal(UpsModelPreset.ForzaFx2200Lcd, fxOptions.SelectedModel);

        InvalidLaunchOptions invalid = Assert.IsType<InvalidLaunchOptions>(
            ApplicationOptionsParser.Parse(["--model", "unknown"]));
        Assert.Contains("nt2401pro", invalid.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("fx2200", invalid.ErrorMessage, StringComparison.Ordinal);

        TrayMonitorLaunchOptions shortMode = Assert.IsType<TrayMonitorLaunchOptions>(
            ApplicationOptionsParser.Parse(["--icon-mode", "short"]));
        Assert.Equal(TrayIconMode.Short, shortMode.SelectedIconMode);

        TrayMonitorLaunchOptions largeMode = Assert.IsType<TrayMonitorLaunchOptions>(
            ApplicationOptionsParser.Parse(["--icon-mode", "large"]));
        Assert.Equal(TrayIconMode.Large, largeMode.SelectedIconMode);

        Assert.IsType<InvalidLaunchOptions>(ApplicationOptionsParser.Parse(["--icon-mode", "tiny"]));
    }

    [Fact]
    public void PopoverViewModel_WithForzaNt2401ProProfile_RendersModelTitleBatLabelsAnd1200WCeiling()
    {
        UpsModelProfile profile = UpsModelCatalog.GetProfile(UpsModelPreset.ForzaNt2401Pro);
        UpsTelemetry telemetry = UpsTelemetry.CreateDefault() with
        {
            LoadPercentage = 18,
            ActiveLoadWatts = 216,
            BankSpec = profile.BankSpec
        };

        PopoverViewModel viewModel = PopoverViewModel.FromTelemetry(telemetry, CultureInfo.InvariantCulture);

        Assert.Equal("Forza NT-2401PRO", viewModel.Title);
        Assert.Contains("Bat 1", viewModel.CellsSummaryText, StringComparison.Ordinal);
        Assert.Contains("Bat 2", viewModel.CellsSummaryText, StringComparison.Ordinal);
        Assert.Equal("(216 W of 1200W)", viewModel.ActiveLoadCeilingText);
    }
}
