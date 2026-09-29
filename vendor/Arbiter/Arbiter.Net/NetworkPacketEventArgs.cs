namespace Arbiter.Net;

public class NetworkPacketEventArgs(NetworkPacket packet)
    : EventArgs
{
    public NetworkPacket Packet { get; } = packet;
}