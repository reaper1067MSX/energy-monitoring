using VoltTray.Core.Models;
using VoltTray.UI.Models;
using VoltTray.UI.Views;

namespace VoltTray.UI.Presenters;

public class SplitCockpitPresenter
{
    private readonly ITrayIconView _trayIcon;
    private readonly IPopoverView _popover;
    private readonly Func<bool> _toggleBeeper;
    private readonly Func<bool> _triggerSelfTest;

    public UpsTelemetry? CurrentTelemetry { get; private set; }

    public SplitCockpitPresenter(
        ITrayIconView trayIcon,
        IPopoverView popover,
        Func<bool> toggleBeeper,
        Func<bool> triggerSelfTest)
    {
        _trayIcon = trayIcon ?? throw new ArgumentNullException(nameof(trayIcon));
        _popover = popover ?? throw new ArgumentNullException(nameof(popover));
        _toggleBeeper = toggleBeeper ?? throw new ArgumentNullException(nameof(toggleBeeper));
        _triggerSelfTest = triggerSelfTest ?? throw new ArgumentNullException(nameof(triggerSelfTest));
    }

    public void UpdateTelemetry(UpsTelemetry telemetry, int dpi = 96)
    {
        CurrentTelemetry = telemetry;

        _trayIcon.UpdateIcon(telemetry.State, telemetry.BatteryPercentage, dpi);
        _trayIcon.SetTooltip(FormatTooltip(telemetry.State, telemetry.BatteryPercentage, telemetry.EstimatedRuntimeMinutes));
    }

    public void TogglePopover(ViewRect iconRect, TaskbarEdge edge, ViewRect workArea, UITheme theme)
    {
        if (_popover.IsVisible)
        {
            _popover.Hide();
        }
        else if (CurrentTelemetry != null)
        {
            _popover.Show(CurrentTelemetry, theme, iconRect, edge, workArea);
        }
    }

    public bool ToggleBeeper() => _toggleBeeper();

    public bool TriggerSelfTest() => _triggerSelfTest();

    public static (double Cell1, double Cell2) DeriveSymmetricCells(double totalVoltageDc)
    {
        if (totalVoltageDc <= 0.0)
            return (0.0, 0.0);

        double half = Math.Round(totalVoltageDc / 2.0, 2);
        return (half, half);
    }

    public static int CalculateActiveLoadWatts(int loadPercentage, int ratedWatts = 900)
    {
        int clamped = Math.Clamp(loadPercentage, 0, 100);
        return (int)Math.Round((double)clamped * ratedWatts / 100.0, MidpointRounding.AwayFromZero);
    }

    public static string FormatTooltip(UpsOperationalState state, int batteryPercentage, int? runtimeMinutes)
    {
        string? runtimeSegment = RuntimeDisplayFormatter.FormatRuntimeString(runtimeMinutes);
        string metricsSegment = runtimeSegment is null
            ? $"{batteryPercentage}%"
            : $"{batteryPercentage}% · {runtimeSegment}";

        return state switch
        {
            UpsOperationalState.LineNormal => $"Forza FX-2200 LCD · Online ({metricsSegment})",
            UpsOperationalState.OnBattery => $"Forza FX-2200 LCD · On Battery ({metricsSegment})",
            UpsOperationalState.BatteryLow => $"Forza FX-2200 LCD · Battery Low ({metricsSegment})",
            UpsOperationalState.ChargingAvr => $"Forza FX-2200 LCD · AVR Boost ({metricsSegment})",
            UpsOperationalState.Fault => "Forza FX-2200 LCD · Fault (Check Unit)",
            UpsOperationalState.Paused => "Forza FX-2200 LCD · Monitoring Paused",
            _ => "Forza FX-2200 LCD · No Communication"
        };
    }
}
