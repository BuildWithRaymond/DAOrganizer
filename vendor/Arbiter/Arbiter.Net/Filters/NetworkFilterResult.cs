using System.Collections.Immutable;

namespace Arbiter.Net.Filters;

public class NetworkFilterResult
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public ImmutableArray<string> FilterChain { get; private set; } = [];
    public NetworkFilterAction Action { get; set; }
    public required NetworkPacket Input { get; set; }
    public NetworkPacket? Output { get; set; }
    public Exception? Exception { get; set; }
    
    internal void AddFilterName(string name)
    {
        FilterChain = FilterChain.Add(name);
    }
}