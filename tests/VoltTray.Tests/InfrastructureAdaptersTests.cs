using VoltTray.Core.Ports;
using VoltTray.Infrastructure.Hid;
using VoltTray.Infrastructure.OS;

namespace VoltTray.Tests;

public class InfrastructureAdaptersTests
{
    [Fact]
    public void WindowsShutdownPort_DryRun_DoesNotTerminateOS_AndFiresCallback()
    {
        var port = new WindowsShutdownPort();
        string? message = null;
        port.OnDryRunExecuted = msg => message = msg;

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
    public async Task Win32HidUpsPort_QueryAsync_WhenDeviceUnavailable_ReturnsNullWithoutThrowing()
    {
        using var port = new Win32HidUpsPort();
        using var cts = new CancellationTokenSource(100);

        string? response = await port.QueryAsync("QS\r", cts.Token);

        Assert.Null(response);
    }
}
