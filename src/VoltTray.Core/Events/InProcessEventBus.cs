using VoltTray.Core.Ports;

namespace VoltTray.Core.Events;

/// <summary>
/// Synchronous, ordered in-process event bus. Each handler runs inside the
/// execution boundary so one failing subscriber never blocks the others.
/// </summary>
public sealed class InProcessEventBus : IEventBus
{
    private readonly IExecutionBoundary _executionBoundary;
    private readonly object _gate = new();
    private readonly Dictionary<Type, List<Delegate>> _handlersByEventType = new();

    public InProcessEventBus(IExecutionBoundary executionBoundary)
    {
        _executionBoundary = executionBoundary ?? throw new ArgumentNullException(nameof(executionBoundary));
    }

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_gate)
        {
            if (!_handlersByEventType.TryGetValue(typeof(TEvent), out List<Delegate>? handlers))
            {
                handlers = new List<Delegate>();
                _handlersByEventType[typeof(TEvent)] = handlers;
            }

            handlers.Add(handler);
        }

        return new Subscription(() => Unsubscribe(typeof(TEvent), handler));
    }

    public void Publish<TEvent>(TEvent eventData)
    {
        Delegate[] snapshot;
        lock (_gate)
        {
            snapshot = _handlersByEventType.TryGetValue(typeof(TEvent), out List<Delegate>? handlers)
                ? handlers.ToArray()
                : [];
        }

        string operationName = $"{nameof(InProcessEventBus)}.{typeof(TEvent).Name}";
        foreach (Action<TEvent> handler in snapshot)
        {
            _executionBoundary.Execute(() => handler(eventData), operationName);
        }
    }

    private void Unsubscribe(Type eventType, Delegate handler)
    {
        lock (_gate)
        {
            if (_handlersByEventType.TryGetValue(eventType, out List<Delegate>? handlers))
            {
                handlers.Remove(handler);
            }
        }
    }

    private sealed class Subscription(Action onDispose) : IDisposable
    {
        private Action? _onDispose = onDispose;

        public void Dispose()
        {
            Interlocked.Exchange(ref _onDispose, null)?.Invoke();
        }
    }
}
