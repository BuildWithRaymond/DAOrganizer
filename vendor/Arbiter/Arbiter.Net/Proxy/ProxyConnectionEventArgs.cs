namespace Arbiter.Net.Proxy;

public class ProxyConnectionEventArgs(ProxyConnection connection) : EventArgs
{
    public ProxyConnection Connection { get; } = connection;
}