using VoltTray.Core.Events;
using VoltTray.Core.Services;

namespace VoltTray.Subscribers;

/// <summary>Feeds every accepted telemetry sample into the shutdown safety rules.</summary>
public sealed class ShutdownSubscriber : IDisposable
{
    private readonly IDisposable _subscription;

    public ShutdownSubscriber(IEventBus eventBus, ShutdownCoordinator shutdownCoordinator)
    {
        ArgumentNullException.ThrowIfNull(eventBus);
        ArgumentNullException.ThrowIfNull(shutdownCoordinator);

        _subscription = eventBus.Subscribe<TelemetryReceived>(
            received => shutdownCoordinator.Evaluate(received.Telemetry, received.Timestamp));
    }

    public void Dispose()
    {
        _subscription.Dispose();
    }
}
