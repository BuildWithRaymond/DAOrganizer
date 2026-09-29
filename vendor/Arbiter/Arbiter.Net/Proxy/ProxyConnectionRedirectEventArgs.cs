using System.Net;

namespace Arbiter.Net.Proxy;

public class ProxyConnectionRedirectEventArgs(ProxyConnection connection, string name, IPEndPoint remoteEndpoint)
    : ProxyConnectionEventArgs(connection)
{
    public string Name { get; } = name;
    public IPEndPoint RemoteEndpoint { get; } = remoteEndpoint;
}