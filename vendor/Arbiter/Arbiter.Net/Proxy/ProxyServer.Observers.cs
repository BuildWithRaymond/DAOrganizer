using System.Collections.Concurrent;
using Arbiter.Net.Observers;

namespace Arbiter.Net.Proxy;

public partial class ProxyServer
{
    private sealed class GlobalObserverRegistration
    {
        public required Func<ProxyConnection, NetworkObserverRef> RegisterOnConnection { get; init; }
        public ConcurrentDictionary<long, NetworkObserverRef> ConnectionRefs { get; } = [];
    }

    private readonly ReaderWriterLockSlim _observersLock = new();
    private readonly List<GlobalObserverRegistration> _globalObservers = [];

    public NetworkObserverRef AddObserver<T>(NetworkMessageObserver<T> observer, int priority = 10,
        object? parameter = null)
        where T : class, INetworkMessage
    {
        CheckIfDisposed();

        var global = new GlobalObserverRegistration
        {
            RegisterOnConnection = c => c.AddObserver(observer, priority, parameter)
        };

        return AddGlobalObserver(global);
    }

    public NetworkObserverRef AddObserver<T>(AsyncNetworkMessageObserver<T> observer, int priority = 10,
        object? parameter = null)
        where T : class, INetworkMessage
    {
        CheckIfDisposed();

        var global = new GlobalObserverRegistration
        {
            RegisterOnConnection = c => c.AddObserver(observer, priority, parameter)
        };

        return AddGlobalObserver(global);
    }

    private NetworkObserverRef AddGlobalObserver(GlobalObserverRegistration global)
    {
        // Store globally so that future clients inherit it
        _observersLock.EnterWriteLock();
        try
        {
            _globalObservers.Add(global);
        }
        finally
        {
            _observersLock.ExitWriteLock();
        }

        // Register on all existing connections
        using (_connectionsLock.EnterScope())
        {
            foreach (var connection in _connections)
            {
                var observerRef = global.RegisterOnConnection(connection);
                global.ConnectionRefs[connection.Id] = observerRef;
            }
        }

        // Return a ref that unregisters from all clients and removes the global registration
        return new NetworkObserverRef(() => UnregisterGlobalObserver(global));
    }

    private void InheritObservers(ProxyConnection connection)
    {
        _observersLock.EnterReadLock();
        try
        {
            foreach (var global in _globalObservers)
            {
                var observerRef = global.RegisterOnConnection(connection);
                global.ConnectionRefs[connection.Id] = observerRef;
            }
        }
        finally
        {
            _observersLock.ExitReadLock();
        }
    }

    private void UnregisterGlobalObserver(GlobalObserverRegistration global)
    {
        // Remove from global list so future clients won't inherit it
        _observersLock.EnterWriteLock();
        try
        {
            _globalObservers.Remove(global);
        }
        finally
        {
            _observersLock.ExitWriteLock();
        }

        using var _ = _connectionsLock.EnterScope();

        // Unregister from all current connections
        foreach (var connection in _connections)
        {
            if (!global.ConnectionRefs.TryRemove(connection.Id, out var observerRef))
            {
                continue;
            }

            if (observerRef.IsRegistered)
            {
                observerRef.Unregister();
            }
        }
    }
}