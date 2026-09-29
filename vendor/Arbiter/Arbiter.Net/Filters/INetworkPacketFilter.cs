using Arbiter.Net.Proxy;

namespace Arbiter.Net.Filters;

public delegate NetworkPacket? NetworkFilterHandler(ProxyConnection connection, NetworkPacket packet,
    object? parameter = null);

public interface INetworkPacketFilter
{
    string? Name { get; }
    int Priority { get;  }
    object? Parameter { get; }
    bool IsEnabled { get; set; }
    NetworkFilterHandler Handler { get; }
}