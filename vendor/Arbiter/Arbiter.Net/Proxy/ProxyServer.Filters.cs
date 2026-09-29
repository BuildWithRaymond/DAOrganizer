using Arbiter.Net.Client;
using Arbiter.Net.Client.Messages;
using Arbiter.Net.Filters;
using Arbiter.Net.Server;
using Arbiter.Net.Server.Messages;

namespace Arbiter.Net.Proxy;

public partial class ProxyServer
{
    private readonly ReaderWriterLockSlim _filtersLock = new();
    private readonly NetworkPacketFilterCollection _clientFilters = new();
    private readonly NetworkPacketFilterCollection _serverFilters = new();

    private readonly ClientMessageFactory _clientMessageFactory = ClientMessageFactory.Default;
    private readonly ServerMessageFactory _serverMessageFactory = ServerMessageFactory.Default;

    public NetworkFilterRef AddFilter<T>(ClientMessageFilterHandler<T> handler, string name, int priority,
        object? parameter = null) where T : IClientMessage
    {
        CheckIfDisposed();

        var serverFilter = new ClientMessageFilter<T>(handler, parameter)
        {
            Name = name,
            Priority = priority
        };

        return AddFilter(serverFilter);
    }

    public NetworkFilterRef AddFilter<T>(ClientMessageFilter<T> filter) where T : IClientMessage
    {
        CheckIfDisposed();

        var messageType = typeof(T);
        var command = _clientMessageFactory.GetMessageCommand(messageType);

        if (command is null)
        {
            throw new ArgumentException($"Message type {messageType.FullName} is not a registered client message type.",
                nameof(filter));
        }

        return AddFilter(command.Value, filter);
    }

    public NetworkFilterRef AddFilter<T>(ServerMessageFilterHandler<T> handler, string name, int priority,
        object? parameter = null) where T : IServerMessage
    {
        CheckIfDisposed();

        var serverFilter = new ServerMessageFilter<T>(handler, parameter)
        {
            Name = name,
            Priority = priority
        };

        return AddFilter(serverFilter);
    }

    public NetworkFilterRef AddFilter<T>(ServerMessageFilter<T> filter) where T : IServerMessage
    {
        CheckIfDisposed();

        var messageType = typeof(T);
        var command = _serverMessageFactory.GetMessageCommand(messageType);

        if (command is null)
        {
            throw new ArgumentException($"Message type {messageType.FullName} is not a registered server message type.",
                nameof(filter));
        }

        return AddFilter(command.Value, filter);
    }

    public NetworkFilterRef AddFilter(ClientCommand command, INetworkPacketFilter filter)
    {
        CheckIfDisposed();
        return AddFilterInternal(ProxyDirection.ClientToServer, (byte)command, filter);
    }

    public NetworkFilterRef AddFilter(ServerCommand command, INetworkPacketFilter filter)
    {
        CheckIfDisposed();
        return AddFilterInternal(ProxyDirection.ServerToClient, (byte)command, filter);
    }

    public bool RemoveFilter(ClientCommand command, string name)
    {
        CheckIfDisposed();
        return RemoveFilterInternal(ProxyDirection.ClientToServer, (byte)command, name);
    }

    public bool RemoveFilter(ServerCommand command, string name)
    {
        CheckIfDisposed();
        return RemoveFilterInternal(ProxyDirection.ServerToClient, (byte)command, name);
    }

    public NetworkFilterRef AddGlobalFilter(ProxyDirection direction, INetworkPacketFilter filter)
    {
        CheckIfDisposed();
        return AddFilterInternal(direction, null, filter);
    }

    public bool RemoveGlobalFilter(ProxyDirection direction, string name)
    {
        CheckIfDisposed();
        return RemoveFilterInternal(direction, null, name);
    }

    private NetworkFilterRef AddFilterInternal(ProxyDirection direction, byte? command, INetworkPacketFilter filter)
    {
        var filters = direction switch
        {
            ProxyDirection.ClientToServer => _clientFilters,
            ProxyDirection.ServerToClient => _serverFilters,
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
        };

        _filtersLock.EnterWriteLock();
        try
        {
            if (command.HasValue)
            {
                filters.AddFilter(command.Value, filter);
            }
            else
            {
                filters.AddGlobalFilter(filter);
            }
        }
        finally
        {
            _filtersLock.ExitWriteLock();
        }

        using var _ = _connectionsLock.EnterScope();

        foreach (var connection in _connections)
        {
            if (direction == ProxyDirection.ClientToServer)
            {
                // Add to the client filter collection
                if (command.HasValue)
                {
                    connection.AddFilter((ClientCommand)command.Value, filter);
                }
                else
                {
                    connection.AddGlobalFilter(ProxyDirection.ClientToServer, filter);
                }
            }
            else
            {
                // Add to the server filter collection
                if (command.HasValue)
                {
                    connection.AddFilter((ServerCommand)command.Value, filter);
                }
                else
                {
                    connection.AddGlobalFilter(ProxyDirection.ServerToClient, filter);
                }
            }
        }

        // Return a reference to the filter so it can be removed or toggled later
        var filterRef = new NetworkFilterRef(
            setEnabledAction: enabled => filter.IsEnabled = enabled,
            unregisterAction: () => RemoveFilterInternal(direction, command, filter.Name ?? string.Empty));

        return filterRef;
    }

    private bool RemoveFilterInternal(ProxyDirection direction, byte? command, string name)
    {
        var filters = direction switch
        {
            ProxyDirection.ClientToServer => _clientFilters,
            ProxyDirection.ServerToClient => _serverFilters,
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
        };

        _filtersLock.EnterWriteLock();
        try
        {
            if (command.HasValue)
            {
                filters.RemoveFilter(command.Value, name);
            }
            else
            {
                filters.RemoveGlobalFilter(name);
            }
        }
        finally
        {
            _filtersLock.ExitWriteLock();
        }

        var wasRemoved = false;

        using var _ = _connectionsLock.EnterScope();

        if (direction == ProxyDirection.ClientToServer)
        {
            // Remove from the client filter collection
            wasRemoved = _connections.Aggregate(wasRemoved, (current, connection) => (command.HasValue
                ? connection.RemoveFilter((ClientCommand)command.Value, name)
                : connection.RemoveGlobalFilter(ProxyDirection.ClientToServer, name)) || current);
        }
        else
        {
            // Remove from the server filter collection
            wasRemoved = _connections.Aggregate(wasRemoved, (current, connection) => (command.HasValue
                ? connection.RemoveFilter((ServerCommand)command.Value, name)
                : connection.RemoveGlobalFilter(ProxyDirection.ServerToClient, name)) || current);
        }

        return wasRemoved;
    }

    private void InheritFilters(ProxyConnection connection)
    {
        _filtersLock.EnterReadLock();

        try
        {
            for (var i = 0; i < byte.MaxValue; i++)
            {
                var command = (byte)i;
                var clientFilters = _clientFilters.GetFilters(command);
                var serverFilters = _serverFilters.GetFilters(command);

                foreach (var filter in clientFilters)
                {
                    connection.AddFilter((ClientCommand)command, filter);
                }

                foreach (var filter in serverFilters)
                {
                    connection.AddFilter((ServerCommand)command, filter);
                }
            }
        }
        finally
        {
            _filtersLock.ExitReadLock();
        }
    }
}