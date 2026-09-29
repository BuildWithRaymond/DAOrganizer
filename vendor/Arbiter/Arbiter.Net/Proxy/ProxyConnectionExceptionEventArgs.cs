namespace Arbiter.Net.Proxy;

public class ProxyConnectionExceptionEventArgs(ProxyConnection connection, NetworkPacket packet)
    : ProxyConnectionEventArgs(connection)
{
    public NetworkPacket Packet { get; } = packet;
}