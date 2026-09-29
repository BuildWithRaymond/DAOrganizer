using Arbiter.Net.Filters;

namespace Arbiter.Net.Proxy;

public class ProxyConnectionDataEventArgs(
    ProxyConnection connection,
    NetworkDirection direction,
    NetworkPacket encrypted,
    NetworkPacket decrypted,
    NetworkFilterResult? filterResult = null)
    : ProxyConnectionEventArgs(connection)
{
    public NetworkDirection Direction { get; } = direction;
    public NetworkPacket Encrypted { get; } = encrypted;
    public NetworkPacket Decrypted { get; } = decrypted;
    public NetworkFilterResult? FilterResult { get; } = filterResult;
}