namespace VoltTray.Core.Events;

/// <summary>
/// In-process publish/subscribe channel that lets producers announce facts
/// without knowing which consumers react to them.
/// </summary>
public interface IEventBus
{
    IDisposable Subscribe<TEvent>(Action<TEvent> handler);

    void Publish<TEvent>(TEvent eventData);
}
