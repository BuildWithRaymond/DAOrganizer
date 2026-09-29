using Arbiter.Net.Client;
using Arbiter.Net.Observers;
using Arbiter.Net.Server;

namespace Arbiter.Net.Proxy;

public partial class ProxyConnection
{
    private readonly NetworkObserverDispatcher _observerDispatcher = new();

    public NetworkObserverRef AddObserver<T>(NetworkMessageObserver<T> observer, int priority = 10, object? parameter = null)
        where T : class, INetworkMessage
    {
        CheckIfDisposed();
        return _observerDispatcher.AddObserver(observer, priority, parameter);
    }
    
    public NetworkObserverRef AddObserver<T>(AsyncNetworkMessageObserver<T> observer, int priority = 10, object? parameter = null)
        where T : class, INetworkMessage
    {
        CheckIfDisposed();
        return _observerDispatcher.AddObserver(observer, priority, parameter);
    }
    
    private void NotifyObservers(ProxyConnection connection, NetworkPacket packet)
    {
        switch (packet)
        {
            case ServerPacket serverPacket:
            {
                if (!_serverMessageFactory.TryCreate(serverPacket, out var message))
                {
                    return;
                }

                _observerDispatcher.TryNotify(connection, message);
                break;
            }
            case ClientPacket clientPacket:
            {
                if (!_clientMessageFactory.TryCreate(clientPacket, out var message))
                {
                    return;
                }

                _observerDispatcher.TryNotify(connection, message);
                break;
            }
        }
    }
}