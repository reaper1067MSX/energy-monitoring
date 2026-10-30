using System;
using System.Collections.Generic;
using VoltTray.Core.Ports;
using VoltTray.Core.Services;
using Xunit;

namespace VoltTray.Tests;

public class ShutdownCoordinatorTests
{
    private class FakeShutdownPort : ISafetyShutdownPort
    {
        public int InitiateShutdownCalls { get; private set; }
        public int AbortShutdownCalls { get; private set; }
        public bool LastDryRun { get; private set; }
        public string? LastReason { get; private set; }

        public bool InitiateShutdown(bool dryRun, string reason)
        {
            InitiateShutdownCalls++;
            LastDryRun = dryRun;
            LastReason = reason;
            return true;
        }

        public bool AbortShutdown()
        {
            AbortShutdownCalls++;
            return true;
        }
    }

    private class FakeNotificationPort : INotificationPort
    {
        public List<(string Title, string Message, NotificationSeverity Severity)> Notifications { get; } = new();

        public void ShowNotification(string title, string message, NotificationSeverity severity)
        {
            Notifications.Add((title, message, severity));
        }
    }

    private readonly FakeShutdownPort _shutdownPort = new();
    private readonly FakeNotificationPort _notificationPort = new();
    private readonly ShutdownCoordinator _coordinator;

    public ShutdownCoordinatorTests()
    {
        _coordinator = new ShutdownCoordinator(_shutdownPort, _notificationPort)
        {
            DryRun = false // default to false in specific test unless testing dry-run
        };
    }

    [Fact]
    public void Evaluate_MainsPresent_LowBattery_SuppressesShutdownAndEmitsWeakWarning()
    {
        _coordinator.Evaluate(onBattery: false, lowBattery: true, runtimeMinutes: 25, isTestInProgress: false);

        Assert.Equal(0, _shutdownPort.InitiateShutdownCalls);
        Assert.Single(_notificationPort.Notifications);
        Assert.Equal("Weak battery warning", _notificationPort.Notifications[0].Title);
        Assert.Equal(NotificationSeverity.Warning, _notificationPort.Notifications[0].Severity);
    }

    [Fact]
    public void Evaluate_SingleCycleGlitch_DoesNotTriggerShutdown()
    {
        // 1 cycle of outage glitch
        _coordinator.Evaluate(onBattery: true, lowBattery: false, runtimeMinutes: 3, isTestInProgress: false);
        Assert.Equal(1, _coordinator.OutageCycleCount);
        Assert.Equal(0, _shutdownPort.InitiateShutdownCalls);

        // Mains restored immediately
        _coordinator.Evaluate(onBattery: false, lowBattery: false, runtimeMinutes: 25, isTestInProgress: false);
        Assert.Equal(0, _coordinator.OutageCycleCount);
        Assert.Equal(0, _shutdownPort.InitiateShutdownCalls);
    }

    [Fact]
    public void Evaluate_ConfirmedOutage_ImmediateLowBatteryTrip_BypassesDebounce()
    {
        // Cycle 1: Outage begins
        _coordinator.Evaluate(onBattery: true, lowBattery: false, runtimeMinutes: 10, isTestInProgress: false);
        Assert.Equal(1, _coordinator.OutageCycleCount);
        Assert.Equal(0, _shutdownPort.InitiateShutdownCalls);

        // Cycle 2: Outage confirmed & Low battery asserted -> immediate trip!
        _coordinator.Evaluate(onBattery: true, lowBattery: true, runtimeMinutes: 1, isTestInProgress: false);
        Assert.Equal(2, _coordinator.OutageCycleCount);
        Assert.Equal(1, _shutdownPort.InitiateShutdownCalls);
        Assert.False(_shutdownPort.LastDryRun);

        // Cycle 3: Still low battery -> does not repeat trigger
        _coordinator.Evaluate(onBattery: true, lowBattery: true, runtimeMinutes: 1, isTestInProgress: false);
        Assert.Equal(1, _shutdownPort.InitiateShutdownCalls);
    }

    [Fact]
    public void Evaluate_ConfirmedOutage_LowRuntimeDebounce_TriggersOnSecondConsecutiveCycle()
    {
        // Cycle 1: Outage begins
        _coordinator.Evaluate(onBattery: true, lowBattery: false, runtimeMinutes: 10, isTestInProgress: false);
        Assert.Equal(0, _shutdownPort.InitiateShutdownCalls);

        // Cycle 2: Confirmed outage, runtime drops to 4 min (cycle 1 of debounce)
        _coordinator.Evaluate(onBattery: true, lowBattery: false, runtimeMinutes: 4, isTestInProgress: false);
        Assert.Equal(1, _coordinator.LowRuntimeCycleCount);
        Assert.Equal(0, _shutdownPort.InitiateShutdownCalls);

        // Cycle 3: Runtime remains <= 5 min for 2nd consecutive cycle -> triggers shutdown!
        _coordinator.Evaluate(onBattery: true, lowBattery: false, runtimeMinutes: 4, isTestInProgress: false);
        Assert.Equal(2, _coordinator.LowRuntimeCycleCount);
        Assert.Equal(1, _shutdownPort.InitiateShutdownCalls);

        // Cycle 4: Continues in low runtime -> does not trigger again
        _coordinator.Evaluate(onBattery: true, lowBattery: false, runtimeMinutes: 3, isTestInProgress: false);
        Assert.Equal(1, _shutdownPort.InitiateShutdownCalls);
    }

    [Fact]
    public void Evaluate_ConfirmedOutage_RuntimeFluctuation_ResetsDebounce()
    {
        // Outage confirmed
        _coordinator.Evaluate(onBattery: true, lowBattery: false, runtimeMinutes: 10, isTestInProgress: false);
        _coordinator.Evaluate(onBattery: true, lowBattery: false, runtimeMinutes: 4, isTestInProgress: false);
        Assert.Equal(1, _coordinator.LowRuntimeCycleCount);
        Assert.Equal(0, _shutdownPort.InitiateShutdownCalls);

        // Runtime temporarily rises above 5 min -> debounce resets
        _coordinator.Evaluate(onBattery: true, lowBattery: false, runtimeMinutes: 6, isTestInProgress: false);
        Assert.Equal(0, _coordinator.LowRuntimeCycleCount);
        Assert.Equal(0, _shutdownPort.InitiateShutdownCalls);

        // Runtime drops again -> cycle 1 of new debounce
        _coordinator.Evaluate(onBattery: true, lowBattery: false, runtimeMinutes: 4, isTestInProgress: false);
        Assert.Equal(1, _coordinator.LowRuntimeCycleCount);
        Assert.Equal(0, _shutdownPort.InitiateShutdownCalls);
    }

    [Fact]
    public void Evaluate_MainsRestoredBeforeCommit_AbortsShutdownAndEmitsRestoredNotification()
    {
        // Confirmed outage + low battery -> shutdown initiated
        _coordinator.Evaluate(onBattery: true, lowBattery: false, runtimeMinutes: 10, isTestInProgress: false);
        _coordinator.Evaluate(onBattery: true, lowBattery: true, runtimeMinutes: 1, isTestInProgress: false);
        Assert.Equal(1, _shutdownPort.InitiateShutdownCalls);
        Assert.True(_coordinator.IsShutdownInitiated);

        // Mains restores
        _coordinator.Evaluate(onBattery: false, lowBattery: false, runtimeMinutes: 25, isTestInProgress: false);
        Assert.Equal(1, _shutdownPort.AbortShutdownCalls);
        Assert.False(_coordinator.IsShutdownInitiated);

        var restoreNotification = _notificationPort.Notifications.Find(n => n.Title == "Utility power restored");
        Assert.NotNull(restoreNotification.Title);
        Assert.Equal(NotificationSeverity.Info, restoreNotification.Severity);
    }

    [Fact]
    public void Evaluate_SelfTestImmunity_SuppressesShutdownFor15Seconds_ThenTriggersIfOutagePersists()
    {
        var t0 = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

        // Start self-test
        _coordinator.Evaluate(onBattery: false, lowBattery: false, runtimeMinutes: 25, isTestInProgress: true, now: t0);

        // Outage occurs at t = 5s and confirmed at t = 7s during test
        _coordinator.Evaluate(onBattery: true, lowBattery: true, runtimeMinutes: 1, isTestInProgress: true, now: t0.AddSeconds(5));
        _coordinator.Evaluate(onBattery: true, lowBattery: true, runtimeMinutes: 1, isTestInProgress: true, now: t0.AddSeconds(7));

        // Suppressed by 15s immunity!
        Assert.Equal(0, _shutdownPort.InitiateShutdownCalls);

        // At t = 16s, 15s immunity expires, outage and low battery persist -> triggers shutdown!
        _coordinator.Evaluate(onBattery: true, lowBattery: true, runtimeMinutes: 1, isTestInProgress: true, now: t0.AddSeconds(16));
        Assert.Equal(1, _shutdownPort.InitiateShutdownCalls);
    }

    [Fact]
    public void Evaluate_DryRunMode_CallsPortWithDryRunTrueAndEmitsSimulationToast()
    {
        _coordinator.DryRun = true;

        // Outage confirmed + low battery
        _coordinator.Evaluate(onBattery: true, lowBattery: false, runtimeMinutes: 10, isTestInProgress: false);
        _coordinator.Evaluate(onBattery: true, lowBattery: true, runtimeMinutes: 1, isTestInProgress: false);

        Assert.Equal(1, _shutdownPort.InitiateShutdownCalls);
        Assert.True(_shutdownPort.LastDryRun);

        var simToast = _notificationPort.Notifications.Find(n => n.Title == "Shutdown simulation (Dry-Run)");
        Assert.NotNull(simToast.Title);
        Assert.Equal(NotificationSeverity.Info, simToast.Severity);
    }
}
