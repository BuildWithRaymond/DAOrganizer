namespace Arbiter.Net.Filters;

public class NetworkFilterRef : IDisposable
{
    private bool _isDisposed;
    private readonly Action<bool> _setEnabledAction;
    private readonly Action _unregisterAction;

    public bool IsRegistered { get; private set; } = true;

    internal NetworkFilterRef(Action<bool> setEnabledAction, Action unregisterAction)
    {
        _setEnabledAction = setEnabledAction;
        _unregisterAction = unregisterAction;
    }

    ~NetworkFilterRef() => Dispose(false);

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public void SetEnabled(bool enabled)
    {
        CheckIfDisposed();

        if (!IsRegistered)
        {
            throw new InvalidOperationException("Filter has been removed, cannot set enabled state");
        }

        _setEnabledAction(enabled);
    }

    public void Unregister()
    {
        CheckIfDisposed();
        _unregisterAction();

        IsRegistered = false;
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
        => ObjectDisposedException.ThrowIf(_isDisposed, nameof(NetworkFilterRef));
}