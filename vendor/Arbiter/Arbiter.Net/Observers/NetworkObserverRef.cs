namespace Arbiter.Net.Observers;

public class NetworkObserverRef : IDisposable
{
    private readonly Action _unregisterAction;
    private bool _isDisposed;

    public bool IsRegistered { get; private set; } = true;

    internal NetworkObserverRef(Action unregisterAction)
    {
        _unregisterAction = unregisterAction;
    }

    public void Unregister()
    {
        CheckIfDisposed();
        _unregisterAction();

        IsRegistered = false;
    }

    ~NetworkObserverRef() => Dispose(false);

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool isDisposing)
    {
        if (_isDisposed)
        {
            return;
        }

        if (isDisposing)
        {
            if (IsRegistered)
            {
                _unregisterAction();
            }
        }

        IsRegistered = false;
        _isDisposed = true;
    }

    private void CheckIfDisposed()
        => ObjectDisposedException.ThrowIf(_isDisposed, nameof(NetworkObserverRef));
}