using Arbiter.Net.Proxy;
using Arbiter.Net.Serialization;
using Arbiter.Net.Server;
using Arbiter.Net.Server.Messages;

namespace Arbiter.Net.Filters;

public delegate NetworkPacket? ServerMessageFilterHandler<TMessage>(ProxyConnection connection, TMessage message,
    object? parameter, NetworkMessageFilterResult<TMessage> result) where TMessage : IServerMessage;

public delegate bool ServerMessageFilterPredicate<in TMessage>(ProxyConnection connection, TMessage message,
    object? parameter) where TMessage : IServerMessage;

public class ServerMessageFilter<TMessage> : INetworkMessageFilter where TMessage : IServerMessage
{
    private readonly ServerMessageFactory _messageFactory = ServerMessageFactory.Default;
    private readonly ServerMessageFilterHandler<TMessage> _handler;

    public string? Name { get; init; }
    public int Priority { get; init; }
    public bool IsEnabled { get; set; } = true;
    public object? Parameter { get; }

    public NetworkFilterHandler Handler => HandlePacket;

    public ServerMessageFilter(ServerMessageFilterHandler<TMessage> handler, object? parameter = null)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        Parameter = parameter;
    }

    private NetworkPacket? HandlePacket(ProxyConnection connection, NetworkPacket packet, object? parameter)
    {
        // Verify the packet is actually a server packet
        if (packet is not ServerPacket serverPacket)
        {
            return packet;
        }

        // Verify the message can be deserialized from the packet
        if (!_messageFactory.TryCreate<TMessage>(serverPacket, out var message))
        {
            return packet;
        }

        // Create a result helper that allows the user to choose the action to take
        var result = new NetworkMessageFilterResult<TMessage>(
            originalPacketFactory: () => packet,
            modifiedPacketFactory: modifiedMessage =>
            {
                var builder = new NetworkPacketBuilder(modifiedMessage.Command);
                try
                {
                    modifiedMessage.Serialize(ref builder);
                    return builder.ToPacket(packet.Source);
                }
                finally
                {
                    builder.Dispose();
                }
            });

        return _handler(connection, message, Parameter, result);
    }
}