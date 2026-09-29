using Arbiter.Net.Proxy;

namespace Arbiter.Net.Observers;

public delegate Task AsyncNetworkMessageObserver<in T>(ProxyConnection connection, T message, object? parameter) where T: class, INetworkMessage;

internal class AsyncObserverRegistration<T> : IObserverRegistration where T : class, INetworkMessage
{
    private readonly AsyncNetworkMessageObserver<T> _observer;
    private readonly object? _parameter;

    public int Priority { get; init; } = 10;

    public AsyncObserverRegistration(AsyncNetworkMessageObserver<T> observer, object? parameter = null)
    {
        _observer = observer;
        _parameter = parameter;
    }

    public Task HandleAsync(ProxyConnection connection, INetworkMessage message)
    {
        var typedMessage = (T)message;
        return _observer(connection, typedMessage, _parameter);
    }
}