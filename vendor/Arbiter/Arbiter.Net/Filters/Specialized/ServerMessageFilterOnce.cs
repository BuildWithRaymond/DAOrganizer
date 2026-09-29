using Arbiter.Net.Proxy;
using Arbiter.Net.Server.Messages;

namespace Arbiter.Net.Filters.Specialized;

internal sealed class ServerMessageFilterOnce<T> where T: IServerMessage
{
    private readonly ServerMessageFilterPredicate<T> _predicate;
    private readonly ServerMessageFilterHandler<T> _handler;
    private readonly Action? _onTriggered;
    private volatile bool _isTriggered;

    public ServerMessageFilterOnce(
        ServerMessageFilterPredicate<T> predicate,
        ServerMessageFilterHandler<T> handler,
        Action? onTriggered = null)
    {
        _predicate = predicate;
        _handler = handler;
        _onTriggered = onTriggered;
    }

    public NetworkPacket? Handle(ProxyConnection connection, T message, object? parameter,
        NetworkMessageFilterResult<T> result)
    {
        if (_isTriggered || !_predicate(connection, message, parameter))
        {
            return result.Passthrough();
        }

        _isTriggered = true;

        var packet = _handler(connection, message, parameter, result);
        _onTriggered?.Invoke();

        return packet;
    }
}