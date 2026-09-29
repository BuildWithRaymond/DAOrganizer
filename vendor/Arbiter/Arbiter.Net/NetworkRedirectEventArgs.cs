using System.Net;

namespace Arbiter.Net;

public class NetworkRedirectEventArgs(string name, IPEndPoint remoteEndpoint) : EventArgs
{
    public string Name { get; } = name;
    public IPEndPoint RemoteEndpoint { get; } = remoteEndpoint;
}