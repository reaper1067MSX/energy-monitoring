using System;
using VoltTray.Core.Models;
using VoltTray.Core.Ports;

namespace VoltTray.Core.Services;

public sealed class ShutdownCoordinator
{
    private readonly ISafetyShutdownPort _shutdownPort;
    private readonly INotificationPort _notificationPort;

    private int _outageCycleCount;
    private int _lowRuntimeCycleCount;
    private bool _shutdownInitiated;
    private bool _lastTestState;
    private DateTimeOffset? _selfTestStartTime;

    public bool DryRun { get; set; } = true;
    public int RuntimeThresholdMinutes { get; set; } = 5;

    public bool IsShutdownInitiated => _shutdownInitiated;
    public int OutageCycleCount => _outageCycleCount;
    public int LowRuntimeCycleCount => _lowRuntimeCycleCount;

    public ShutdownCoordinator(ISafetyShutdownPort shutdownPort, INotificationPort notificationPort)
    {
        _shutdownPort = shutdownPort ?? throw new ArgumentNullException(nameof(shutdownPort));
        _notificationPort = notificationPort ?? throw new ArgumentNullException(nameof(notificationPort));
    }

    public void Evaluate(UpsTelemetry telemetry, DateTimeOffset? now = null)
    {
        if (telemetry == null) return;

        bool onBattery = telemetry.State is UpsOperationalState.OnBattery or UpsOperationalState.BatteryLow;
        bool lowBattery = telemetry.State == UpsOperationalState.BatteryLow;

        Evaluate(onBattery, lowBattery, telemetry.EstimatedRuntimeMinutes, telemetry.IsTestInProgress, now);
    }

    public void Evaluate(bool onBattery, bool lowBattery, int runtimeMinutes, bool isTestInProgress, DateTimeOffset? now = null)
    {
        DateTimeOffset timestamp = now ?? DateTimeOffset.UtcNow;

        if (isTestInProgress)
        {
            if (!_lastTestState)
            {
                _selfTestStartTime = timestamp;
                _lastTestState = true;
            }
        }
        else
        {
            _lastTestState = false;
            _selfTestStartTime = null;
        }

        if (!onBattery)
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
            return;
        }

        _outageCycleCount++;

        if (_outageCycleCount >= 2 && !_shutdownInitiated)
        {
            if (isTestInProgress && _selfTestStartTime.HasValue && (timestamp - _selfTestStartTime.Value).TotalSeconds < 15.0)
            {
                return; // 15-second self-test immunity
            }

            if (lowBattery)
            {
                TriggerShutdown("Critical low battery detected during outage");
            }
            else if (runtimeMinutes <= RuntimeThresholdMinutes)
            {
                _lowRuntimeCycleCount++;
                if (_lowRuntimeCycleCount >= 2)
                {
                    TriggerShutdown($"Estimated runtime depleted ({runtimeMinutes} min <= {RuntimeThresholdMinutes} min)");
                }
            }
            else
            {
                _lowRuntimeCycleCount = 0;
            }
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
