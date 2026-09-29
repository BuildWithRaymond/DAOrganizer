
namespace Arbiter.Net.Filters;

public class NetworkPacketFilter : INetworkPacketFilter
{
    public string? Name { get; init; }
    public int Priority { get; init; } = 10;
    public object? Parameter { get; }
    public bool IsEnabled { get; set; } = true;
    public NetworkFilterHandler Handler { get; }

    public NetworkPacketFilter(NetworkFilterHandler handler, object? parameter = null)
    {
        Handler = handler;
        Parameter = parameter;
    }
}