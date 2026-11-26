using VoltTray.Core.Telemetry;
using VoltTray.Shared;
using VoltTray.UI.Models;
using VoltTray.UI.Tokens;
using VoltTray.UI.Views;

namespace VoltTray.UI.Presenters;

public class SplitCockpitPresenter
{
    private readonly ITrayIconView _trayIcon;
    private readonly IPopoverView _popover;
    private readonly Func<Task<bool>> _toggleBeeper;
    private readonly Func<Task<bool>> _triggerSelfTest;

    public UpsTelemetry? CurrentTelemetry { get; private set; }

    public SplitCockpitPresenter(
        ITrayIconView trayIcon,
        IPopoverView popover,
        Func<Task<bool>> toggleBeeper,
        Func<Task<bool>> triggerSelfTest)
    {
        _trayIcon = trayIcon ?? throw new ArgumentNullException(nameof(trayIcon));
        _popover = popover ?? throw new ArgumentNullException(nameof(popover));
        _toggleBeeper = toggleBeeper ?? throw new ArgumentNullException(nameof(toggleBeeper));
        _triggerSelfTest = triggerSelfTest ?? throw new ArgumentNullException(nameof(triggerSelfTest));
    }

    public void UpdateTelemetry(UpsTelemetry telemetry, int dpi = DesignTokens.Sizes.DefaultDpi, string? modelName = null)
    {
        CurrentTelemetry = telemetry;

        string? resolvedModelName = string.IsNullOrWhiteSpace(modelName)
            ? telemetry.BankSpec.Label
            : modelName;

        _trayIcon.UpdateIcon(telemetry.State, telemetry.BatteryPercentage, dpi);
        _trayIcon.SetTooltip(FormatTooltip(
            telemetry.State,
            telemetry.BatteryPercentage,
            telemetry.EstimatedRuntimeMinutes,
            resolvedModelName));
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

    public Task<bool> ToggleBeeperAsync() => _toggleBeeper();

    public Task<bool> TriggerSelfTestAsync() => _triggerSelfTest();

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

    public static string FormatTooltip(
        UpsOperationalState state,
        int batteryPercentage,
        int? runtimeMinutes,
        string? modelName = null)
    {
        string displayTitle = string.IsNullOrWhiteSpace(modelName)
            ? ApplicationDefaults.DefaultUpsDisplayName
            : modelName;
        string? runtimeSegment = RuntimeDisplayFormatter.FormatRuntimeString(runtimeMinutes);
        string metricsSegment = runtimeSegment is null
            ? $"{batteryPercentage}%"
            : $"{batteryPercentage}% · {runtimeSegment}";

        return state switch
        {
            UpsOperationalState.LineNormal => $"{displayTitle} · Online ({metricsSegment})",
            UpsOperationalState.OnBattery => $"{displayTitle} · On Battery ({metricsSegment})",
            UpsOperationalState.BatteryLow => $"{displayTitle} · Battery Low ({metricsSegment})",
            UpsOperationalState.ChargingAvr => $"{displayTitle} · AVR Boost ({metricsSegment})",
            UpsOperationalState.Fault => $"{displayTitle} · Fault (Check Unit)",
            UpsOperationalState.Paused => $"{displayTitle} · Monitoring Paused",
            _ => $"{displayTitle} · No Communication"
        };
    }
}
