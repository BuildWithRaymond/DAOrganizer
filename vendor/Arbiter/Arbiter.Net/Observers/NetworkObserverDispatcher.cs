using System.Buffers;
using System.Threading.Channels;
using Arbiter.Net.Proxy;

namespace Arbiter.Net.Observers;

public class NetworkObserverDispatcher : IDisposable
{
    private record ObserverMessage(ProxyConnection Connection, INetworkMessage Message);

    private readonly ChannelWriter<ObserverMessage> _queueWriter;
    private readonly ChannelReader<ObserverMessage> _queueReader;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly Task _dispatcherTask;

    private readonly ReaderWriterLockSlim _lock = new();
    private readonly Dictionary<Type, List<IObserverRegistration>> _observersByType = new();

    private bool _isDisposed;

    public event EventHandler<NetworkExceptionEventArgs>? ObserverException;

    public NetworkObserverDispatcher()
    {
        var messageQueue = Channel.CreateUnbounded<ObserverMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        _queueWriter = messageQueue.Writer;
        _queueReader = messageQueue.Reader;
        _cancellationTokenSource = new CancellationTokenSource();
        _dispatcherTask = DispatchLoopAsync(_cancellationTokenSource.Token);
    }

    public bool TryNotify(ProxyConnection connection, INetworkMessage message)
    {
        CheckIfDisposed();
        return _queueWriter.TryWrite(new ObserverMessage(connection, message));
    }

    public NetworkObserverRef AddObserver<T>(NetworkMessageObserver<T> observer, int priority = 10,
        object? parameter = null) where T : class, INetworkMessage
    {
        CheckIfDisposed();

        var registration = new SyncObserverRegistration<T>(observer, parameter) { Priority = priority };
        var messageType = typeof(T);

        AddObserverInternal(messageType, registration);

        return new NetworkObserverRef(() => RemoveObserver(messageType, registration));
    }

    public NetworkObserverRef AddObserver<T>(AsyncNetworkMessageObserver<T> observer, int priority = 10,
        object? parameter = null) where T : class, INetworkMessage
    {
        CheckIfDisposed();

        var registration = new AsyncObserverRegistration<T>(observer, parameter) { Priority = priority };
        var messageType = typeof(T);

        AddObserverInternal(messageType, registration);

        return new NetworkObserverRef(() => RemoveObserver(messageType, registration));
    }

    private void AddObserverInternal(Type messageType, IObserverRegistration observer)
    {
        _lock.EnterWriteLock();

        try
        {
            if (!_observersByType.TryGetValue(messageType, out var observers))
            {
                observers = [];
                _observersByType[messageType] = observers;
            }

            observers.Add(observer);
            observers.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    private bool RemoveObserver(Type messageType, IObserverRegistration observer)
    {
        _lock.EnterWriteLock();

        try
        {
            return _observersByType.TryGetValue(messageType, out var observers) && observers.Remove(observer);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    private async Task DispatchLoopAsync(CancellationToken token)
    {
        await foreach (var message in _queueReader.ReadAllAsync(token))
        {
            var messageType = message.Message.GetType();
            IObserverRegistration[]? observersArray = null;
            var observerCount = 0;

            _lock.EnterReadLock();
            try
            {
                if (_observersByType.TryGetValue(messageType, out var observerList) && observerList.Count > 0)
                {
                    observersArray = ArrayPool<IObserverRegistration>.Shared.Rent(observerList.Count);
                    observerList.CopyTo(observersArray);
                    observerCount = observerList.Count;
                }
            }
            finally
            {
                _lock.ExitReadLock();
            }

            if (observersArray == null || observerCount == 0)
            {
                continue;
            }

            try
            {
                // Process observers sequentially based on priority
                for (var i = 0; i < observerCount; i++)
                {
                    await SafeHandleAsync(observersArray[i], message.Connection, message.Message);
                }
            }
            finally
            {
                ArrayPool<IObserverRegistration>.Shared.Return(observersArray);
            }
        }
    }

    private async Task SafeHandleAsync(IObserverRegistration observer, ProxyConnection connection,
        INetworkMessage message)
    {
        try
        {
            await observer.HandleAsync(connection, message);
        }
        catch (Exception ex)
        {
            ObserverException?.Invoke(observer, new NetworkExceptionEventArgs(ex));
        }
    }

    ~NetworkObserverDispatcher() => Dispose(false);

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
            _queueWriter.TryComplete();
            _cancellationTokenSource.Cancel();
            _dispatcherTask.Wait();
            _cancellationTokenSource.Dispose();
            _queueReader.Completion.Dispose();
        }

        _isDisposed = true;
    }

    private void CheckIfDisposed()
        => ObjectDisposedException.ThrowIf(_isDisposed, nameof(NetworkObserverDispatcher));
}