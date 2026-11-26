using VoltTray.Core.Safety;
using VoltTray.Infrastructure.Hid;
using VoltTray.Infrastructure.OS;
using VoltTray.Infrastructure.Protocol;

namespace VoltTray.Tests;

public class InfrastructureAdaptersTests
{
    private const int HardwareQueryAttemptCount = 3;
    private const int HardwareQueryDelayMs = 100;
    private const double MinimumValidInputVoltageAc = 50.0;
    private const double MinimumValidBatteryVoltageDc = 10.0;
    private const int UnavailableDeviceTimeoutMs = 100;

    [Fact]
    public void WindowsShutdownPort_DryRun_DoesNotTerminateOS_AndFiresCallback()
    {
        var port = new WindowsShutdownPort();
        string? message = null;
        port.OnDryRunExecuted = dryRunMessage => message = dryRunMessage;

        bool result = port.InitiateShutdown(dryRun: true, reason: "Power outage test");

        Assert.True(result);
        Assert.NotNull(message);
        Assert.Contains("[DRY-RUN]", message);
        Assert.Contains("Power outage test", message);
    }

    [Fact]
    public void WindowsNotificationPort_DispatchesNotificationAndRecordsHistory()
    {
        var port = new WindowsNotificationPort();
        WindowsNotificationPort.NotificationRecord? received = null;
        port.NotificationDispatched += record => received = record;

        port.ShowNotification("Batería Baja", "Quedan 3 minutos de autonomía", NotificationSeverity.Warning);

        Assert.NotNull(received);
        Assert.Equal("Batería Baja", received.Title);
        Assert.Equal("Quedan 3 minutos de autonomía", received.Message);
        Assert.Equal(NotificationSeverity.Warning, received.Severity);

        Assert.Single(port.History);
        Assert.Equal("Batería Baja", port.History[0].Title);
    }

    [Fact]
    public async Task Win32HidUpsPort_RealHardwareQuery_SucceedsIfHardwarePresent()
    {
        using var port = new Win32HidUpsPort();
        if (port.TryConnect())
        {
            for (int attemptIndex = 0; attemptIndex < HardwareQueryAttemptCount; attemptIndex++)
            {
                if (attemptIndex > 0)
                {
                    await Task.Delay(HardwareQueryDelayMs);
                }

                var telemetry = await port.ReadTelemetryAsync();
                Assert.NotNull(telemetry);
                Assert.True(telemetry.InputVoltageAc > MinimumValidInputVoltageAc);
                Assert.True(telemetry.BatteryVoltageDc > MinimumValidBatteryVoltageDc);
            }

            await Task.Delay(HardwareQueryDelayMs);
            byte[]? raw = await port.QueryRawAsync(VoltronicProtocolConstants.QueryStatusCommand);
            Assert.NotNull(raw);
            Assert.Equal(VoltronicProtocolConstants.FrameHeaderHash, raw[0]);
        }
    }

    [Fact]
    public void Win32HidUpsPort_InitialState_IsDisconnected()
    {
        using var port = new Win32HidUpsPort();

        Assert.False(port.IsConnected);
        Assert.Null(port.ActiveDevicePath);

        // Disconnect when already disconnected should be safe and idempotent
        port.Disconnect();
        Assert.False(port.IsConnected);
    }

    [Fact]
    public async Task Win32HidUpsPort_ReadTelemetryAsync_WhenDeviceUnavailable_ReturnsNullWithoutThrowing()
    {
        using var port = new Win32HidUpsPort(@"\\?\HID#NON_EXISTENT_UPS_DEVICE", allowAutoDiscovery: false);
        using var cts = new CancellationTokenSource(UnavailableDeviceTimeoutMs);

        var telemetry = await port.ReadTelemetryAsync(cancellationToken: cts.Token);

        Assert.Null(telemetry);
    }

    [Fact]
    public async Task SimulatedUpsDevicePort_GeneratesProtocolTFrame_AndHandlesCommands()
    {
        using var port = new SimulatedUpsDevicePort();

        Assert.True(port.IsConnected);
        Assert.NotNull(port.ActiveDevicePath);

        var initial = await port.ReadTelemetryAsync();
        Assert.NotNull(initial);
        Assert.False(initial.IsTestInProgress);

        bool beeperToggled = await port.ToggleBeeperAsync();
        Assert.True(beeperToggled);
        var afterBeeper = await port.ReadTelemetryAsync();
        Assert.NotNull(afterBeeper);
        Assert.NotEqual(initial.IsBeeperMuted, afterBeeper.IsBeeperMuted);

        bool testStarted = await port.TriggerSelfTestAsync();
        Assert.True(testStarted);

        var testTelemetry = await port.ReadTelemetryAsync();
        Assert.NotNull(testTelemetry);
        Assert.True(testTelemetry.IsTestInProgress);
    }
}
