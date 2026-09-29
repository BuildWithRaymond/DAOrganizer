using Arbiter.Net.Filters;

namespace Arbiter.Net.Proxy;

public class ProxyConnectionFilterEventArgs(ProxyConnection connection, NetworkFilterResult result)
    : ProxyConnectionEventArgs(connection)
{
    public NetworkFilterResult Result { get; } = result;
}