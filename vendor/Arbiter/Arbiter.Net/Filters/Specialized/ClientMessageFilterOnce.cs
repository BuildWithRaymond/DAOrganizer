using Arbiter.Net.Client.Messages;
using Arbiter.Net.Proxy;

namespace Arbiter.Net.Filters.Specialized;

internal sealed class ClientMessageFilterOnce<T> where T: IClientMessage
{
    private readonly ClientMessageFilterPredicate<T> _predicate;
    private readonly ClientMessageFilterHandler<T> _handler;
    private readonly Action? _onTriggered;
    private volatile bool _isTriggered;

    public ClientMessageFilterOnce(
        ClientMessageFilterPredicate<T> predicate,
        ClientMessageFilterHandler<T> handler,
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