using System;
using VoltTray.Core.Models;
using VoltTray.Core.Ports;

namespace VoltTray.Core.Services;

public sealed class ShutdownCoordinator
{
    public const int DefaultRuntimeThresholdMinutes = 5;
    public const int ConfirmationReadingThreshold = 2;
    public const double SelfTestImmunitySeconds = 15.0;
    public const double SelfTestCooldownSeconds = 3.0;
    public const double CriticalBlockVoltageCutoffDc = 10.8;

    private static readonly IReadOnlyList<IShutdownTriggerRule> DefaultTriggerRules =
    [
        new ImmediateHardwareLowTriggerRule(),
        new RuntimeThresholdTriggerRule(),
        new UnconfiguredBlockVoltageTriggerRule()
    ];

    private readonly ISafetyShutdownPort _shutdownPort;
    private readonly INotificationPort _notificationPort;
    private readonly IReadOnlyList<IShutdownTriggerRule> _triggerRules;
    private readonly SelfTestImmunityTracker _selfTestImmunityTracker = new();

    private int _outageCycleCount;
    private int _lowRuntimeCycleCount;
    private bool _shutdownInitiated;

    public bool DryRun { get; set; } = true;
    public int RuntimeThresholdMinutes { get; set; } = DefaultRuntimeThresholdMinutes;

    public bool IsShutdownInitiated => _shutdownInitiated;
    public int OutageCycleCount => _outageCycleCount;
    public int LowRuntimeCycleCount => _lowRuntimeCycleCount;

    public ShutdownCoordinator(
        ISafetyShutdownPort shutdownPort,
        INotificationPort notificationPort,
        IReadOnlyList<IShutdownTriggerRule>? rules = null)
    {
        _shutdownPort = shutdownPort ?? throw new ArgumentNullException(nameof(shutdownPort));
        _notificationPort = notificationPort ?? throw new ArgumentNullException(nameof(notificationPort));
        _triggerRules = rules ?? DefaultTriggerRules;
    }

    public void Evaluate(UpsTelemetry telemetry, DateTimeOffset? now = null)
    {
        if (telemetry == null)
        {
            return;
        }

        bool onBattery = telemetry.PowerStatus is { } status
            ? status.IsOnBattery
            : telemetry.State is UpsOperationalState.OnBattery or UpsOperationalState.BatteryLow;

        bool lowBattery = telemetry.PowerStatus is { } powerStatus
            ? powerStatus.IsBatteryLow
            : telemetry.State == UpsOperationalState.BatteryLow;

        bool isTestInProgress = telemetry.IsTestInProgress || (telemetry.PowerStatus?.IsSelfTestActive ?? false);

        Evaluate(
            onBattery,
            lowBattery,
            telemetry.EstimatedRuntimeMinutes,
            isTestInProgress,
            now,
            telemetry.BatteryVoltageDc,
            telemetry.BankSpec.SeriesBatteryCount);
    }

    public void Evaluate(
        bool onBattery,
        bool lowBattery,
        int? runtimeMinutes,
        bool isTestInProgress,
        DateTimeOffset? now = null,
        double? batteryVoltageDc = null,
        int seriesBatteryCount = 2)
    {
        DateTimeOffset timestamp = now ?? DateTimeOffset.UtcNow;
        _selfTestImmunityTracker.Update(isTestInProgress, timestamp);

        if (!onBattery)
        {
            HandleMainsPresent(lowBattery);
            return;
        }

        _outageCycleCount++;

        if (_outageCycleCount < ConfirmationReadingThreshold || _shutdownInitiated)
        {
            return;
        }

        if (_selfTestImmunityTracker.IsImmunityActive(isTestInProgress, timestamp))
        {
            return;
        }

        var context = new ShutdownEvaluationContext(
            lowBattery,
            runtimeMinutes,
            RuntimeThresholdMinutes,
            batteryVoltageDc,
            seriesBatteryCount);

        foreach (IShutdownTriggerRule rule in _triggerRules)
        {
            ShutdownTriggerEvaluation evaluation = rule.Evaluate(context);
            if (!evaluation.Applies)
            {
                continue;
            }

            if (!evaluation.IsTriggered)
            {
                _lowRuntimeCycleCount = 0;
                return;
            }

            if (!evaluation.RequiresDebounce)
            {
                TriggerShutdown(evaluation.TriggerReason);
                return;
            }

            EvaluateDepletionCycle(true, evaluation.TriggerReason);
            return;
        }

        _lowRuntimeCycleCount = 0;
    }

    private void HandleMainsPresent(bool lowBattery)
    {
        if (_shutdownInitiated)
        {
            _shutdownPort.AbortShutdown();
            _notificationPort.ShowNotification(
                "Utility power restored",
                "Main electrical supply is normal. UPS is operating online.",
                NotificationSeverity.Info);
            _shutdownInitiated = false;
        }

        _outageCycleCount = 0;
        _lowRuntimeCycleCount = 0;

        if (lowBattery)
        {
            _notificationPort.ShowNotification(
                "Weak battery warning",
                "Battery voltage drop detected while utility power is present. Consider replacing batteries.",
                NotificationSeverity.Warning);
        }
    }

    private void EvaluateDepletionCycle(bool isConditionMet, string triggerReason)
    {
        if (isConditionMet)
        {
            _lowRuntimeCycleCount++;
            if (_lowRuntimeCycleCount >= ConfirmationReadingThreshold)
            {
                TriggerShutdown(triggerReason);
            }
        }
        else
        {
            _lowRuntimeCycleCount = 0;
        }
    }

    private void TriggerShutdown(string reason)
    {
        _shutdownInitiated = true;
        _shutdownPort.InitiateShutdown(DryRun, reason);

        if (DryRun)
        {
            _notificationPort.ShowNotification(
                "Shutdown simulation (Dry-Run)",
                "Emergency shutdown condition was met. PC will not shut down due to simulation mode.",
                NotificationSeverity.Info);
        }
        else
        {
            _notificationPort.ShowNotification(
                "Critical low battery",
                "UPS battery reached critical level. Initiating safe system shutdown.",
                NotificationSeverity.Critical);
        }
    }
}
