using VoltTray.Core.Models;
using VoltTray.Core.Views;

namespace VoltTray.Core.Presenters;

public class SplitCockpitPresenter
{
    public const string BeeperToggleCommand = "Q\r";
    public const string SelfTestCommand = "T\r";

    private readonly IVoltTrayView _view;
    private readonly ITrayIconView _trayIcon;
    private readonly IPopoverView _popover;
    private readonly Func<string, bool> _sendCommand;

    public UpsTelemetry? CurrentTelemetry { get; private set; }

    public SplitCockpitPresenter(
        IVoltTrayView view,
        ITrayIconView trayIcon,
        IPopoverView popover,
        Func<string, bool> sendCommand)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _trayIcon = trayIcon ?? throw new ArgumentNullException(nameof(trayIcon));
        _popover = popover ?? throw new ArgumentNullException(nameof(popover));
        _sendCommand = sendCommand ?? throw new ArgumentNullException(nameof(sendCommand));
    }

    public void Initialize() => _view.Initialize();

    public void UpdateTelemetry(UpsTelemetry telemetry, int dpi = 96)
    {
        CurrentTelemetry = telemetry;
        _view.UpdateTelemetry(telemetry);
        _view.SetOperationalState(telemetry.State);

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

    public bool ToggleBeeper() => _sendCommand(BeeperToggleCommand);

    public bool TriggerSelfTest() => _sendCommand(SelfTestCommand);

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

    public static string FormatTooltip(UpsOperationalState state, int batteryPercentage, int runtimeMinutes) => state switch
    {
        UpsOperationalState.LineNormal => $"Forza FX-2200 LCD · Online ({batteryPercentage}% · ≈ {runtimeMinutes} min)",
        UpsOperationalState.OnBattery => $"Forza FX-2200 LCD · On Battery ({batteryPercentage}% · ≈ {runtimeMinutes} min)",
        UpsOperationalState.BatteryLow => $"Forza FX-2200 LCD · Battery Low ({batteryPercentage}% · ≈ {runtimeMinutes} min)",
        UpsOperationalState.ChargingAvr => $"Forza FX-2200 LCD · AVR Boost ({batteryPercentage}% · ≈ {runtimeMinutes} min)",
        UpsOperationalState.Fault => "Forza FX-2200 LCD · Fault (Check Unit)",
        UpsOperationalState.Paused => "Forza FX-2200 LCD · Monitoring Paused",
        _ => "Forza FX-2200 LCD · No Communication"
    };
}
