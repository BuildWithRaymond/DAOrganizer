using Arbiter.Net.Filters;

namespace Arbiter.Net;

public class NetworkTransferEventArgs(
    NetworkDirection direction,
    NetworkPacket encrypted,
    NetworkPacket decrypted,
    NetworkFilterResult? filterResult = null)
    : EventArgs
{
    public NetworkDirection Direction { get; } = direction;
    public NetworkPacket Encrypted { get; } = encrypted;
    public NetworkPacket Decrypted { get; } = decrypted;
    public NetworkFilterResult? FilterResult { get; } = filterResult;
}