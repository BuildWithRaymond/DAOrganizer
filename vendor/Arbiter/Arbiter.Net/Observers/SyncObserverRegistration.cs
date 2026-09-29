using Arbiter.Net.Proxy;

namespace Arbiter.Net.Observers;

public delegate void NetworkMessageObserver<in T>(ProxyConnection connection, T message, object? parameter) where T: class, INetworkMessage;

internal class SyncObserverRegistration<T> : IObserverRegistration where T : class, INetworkMessage
{
    private readonly NetworkMessageObserver<T> _observer;
    private readonly object? _parameter;

    public int Priority { get; init; } = 10;

    public SyncObserverRegistration(NetworkMessageObserver<T> observer, object? parameter = null)
    {
        _observer = observer;
        _parameter = parameter;
    }

    public Task HandleAsync(ProxyConnection connection, INetworkMessage message)
    {
        var typedMessage = (T)message;
        _observer(connection, typedMessage, _parameter);
        return Task.CompletedTask;
    }
}