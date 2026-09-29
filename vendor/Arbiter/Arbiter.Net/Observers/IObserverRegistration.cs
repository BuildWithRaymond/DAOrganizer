using Arbiter.Net.Proxy;

namespace Arbiter.Net.Observers;

internal interface IObserverRegistration
{
    int Priority { get; }
    
    Task HandleAsync(ProxyConnection connection, INetworkMessage message);
}