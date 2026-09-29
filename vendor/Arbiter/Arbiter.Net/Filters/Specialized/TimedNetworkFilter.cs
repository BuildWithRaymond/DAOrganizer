namespace Arbiter.Net.Filters.Specialized;

public sealed class TimedNetworkFilter : IDisposable
{
    private readonly NetworkFilterRef _filterRef;
    private readonly CancellationTokenSource? _cancellationTokenSource;
    private volatile bool _isDisposed;

    public TimedNetworkFilter(NetworkFilterRef filterRef, TimeSpan? expiration = null)
    {
        _filterRef = filterRef;

        if (expiration.HasValue)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _ = StartTimer(expiration.Value);
        }
    }

    private async Task StartTimer(TimeSpan expiration)
    {
        try
        {
            await Task.Delay(expiration, _cancellationTokenSource!.Token);
            Dispose();
        }
        catch
        {
            // Do nothing
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();

        _filterRef.Unregister();
    }
}