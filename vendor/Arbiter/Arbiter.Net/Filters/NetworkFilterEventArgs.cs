namespace Arbiter.Net.Filters;

public class NetworkFilterEventArgs : EventArgs
{
    public NetworkFilterResult Result { get; }

    public NetworkFilterEventArgs(NetworkFilterResult result)
    {
        Result = result;
    }
}