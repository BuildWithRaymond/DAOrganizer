using Arbiter.Net.Client;
using Arbiter.Net.Client.Messages;
using Arbiter.Net.Proxy;
using Arbiter.Net.Serialization;

namespace Arbiter.Net.Filters;

public delegate NetworkPacket? ClientMessageFilterHandler<TMessage>(ProxyConnection connection, TMessage message,
    object? parameter, NetworkMessageFilterResult<TMessage> result) where TMessage : IClientMessage;

public delegate bool ClientMessageFilterPredicate<in TMessage>(ProxyConnection connection, TMessage message,
    object? parameter) where TMessage : IClientMessage;

public class ClientMessageFilter<TMessage> : INetworkMessageFilter where TMessage : IClientMessage
{
    private readonly ClientMessageFactory _messageFactory = ClientMessageFactory.Default;
    private readonly ClientMessageFilterHandler<TMessage> _handler;

    public string? Name { get; init; }
    public int Priority { get; init; }
    public bool IsEnabled { get; set; } = true;   
    public object? Parameter { get; }

    public NetworkFilterHandler Handler => HandlePacket;

    public ClientMessageFilter(ClientMessageFilterHandler<TMessage> handler, object? parameter = null)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        Parameter = parameter;
    }

    private NetworkPacket? HandlePacket(ProxyConnection connection, NetworkPacket packet, object? parameter)
    {
        // Verify the packet is actually a client packet
        if (packet is not ClientPacket clientPacket)
        {
            return packet;
        }

        // Verify the message can be deserialized from the packet
        if (!_messageFactory.TryCreate<TMessage>(clientPacket, out var message))
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