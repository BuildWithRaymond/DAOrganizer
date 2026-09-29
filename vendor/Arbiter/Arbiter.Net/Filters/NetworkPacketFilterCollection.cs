using System.Collections.Immutable;

namespace Arbiter.Net.Filters;

public class NetworkPacketFilterCollection
{
    private readonly ReaderWriterLockSlim _lock = new();

    private readonly ImmutableArray<INetworkPacketFilter>[] _filters =
        new ImmutableArray<INetworkPacketFilter>[byte.MaxValue];

    public NetworkPacketFilterCollection()
    {
        InitializeFilters();
    }

    private void InitializeFilters()
    {
        for (var i = 0; i < _filters.Length; i++)
        {
            _filters[i] = ImmutableArray<INetworkPacketFilter>.Empty;
        }
    }

    public NetworkFilterRef AddFilter(byte command, INetworkPacketFilter filter)
    {
        _lock.EnterWriteLock();
        try
        {
            // If a filter with same name exists, remove it first
            if (!string.IsNullOrEmpty(filter.Name))
            {
                var existingFilter = _filters[command].FirstOrDefault(f => f.Name == filter.Name);
                if (existingFilter is not null)
                {
                    _filters[command] = _filters[command].Remove(existingFilter);
                }
            }

            // Add the filter to the list, keep sorted by priority (highest first)
            var filters = _filters[command].Add(filter);
            _filters[command] = filters.Sort((a, b) => b.Priority.CompareTo(a.Priority));

            // Return a reference to the filter so it can be removed or toggled later
            var filterRef = new NetworkFilterRef(
                setEnabledAction: enabled => filter.IsEnabled = enabled,
                unregisterAction: () => RemoveFilter(command, filter.Name ?? string.Empty));

            return filterRef;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public IEnumerable<INetworkPacketFilter> GetFilters(byte command)
    {
        _lock.EnterReadLock();
        try
        {
            return _filters[command];
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public NetworkFilterRef AddGlobalFilter(INetworkPacketFilter filter)
    {
        _lock.EnterWriteLock();
        try
        {
            // Global filters are just applied to all commands
            for (var i = 0; i < _filters.Length; i++)
            {
                // If a filter with same name exists, remove it first
                if (!string.IsNullOrEmpty(filter.Name))
                {
                    var existingFilter = _filters[i].FirstOrDefault(f => f.Name == filter.Name);
                    if (existingFilter is not null)
                    {
                        _filters[i] = _filters[i].Remove(existingFilter);
                    }
                }

                // Add the filter to the list, keep sorted by priority (highest first)
                var filters = _filters[i].Add(filter);
                _filters[i] = filters.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            }
            
            // Return a reference to the filter so it can be removed or toggled later
            var filterRef = new NetworkFilterRef(
                setEnabledAction: enabled => filter.IsEnabled = enabled,
                unregisterAction: () => RemoveGlobalFilter(filter.Name ?? string.Empty));

            return filterRef;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public bool RemoveFilter(byte command, string name)
    {
        _lock.EnterWriteLock();
        try
        {
            var filter = _filters[command].FirstOrDefault(f => f.Name is not null && f.Name == name);
            if (filter is null)
            {
                return false;
            }

            _filters[command] = _filters[command].Remove(filter);
            return true;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public bool RemoveGlobalFilter(string name)
    {
        _lock.EnterWriteLock();
        try
        {
            var wasRemoved = false;

            // Global filters are just applied to all commands
            for (var i = 0; i < _filters.Length; i++)
            {
                var filter = _filters[i].FirstOrDefault(f => f.Name is not null && f.Name == name);
                if (filter is null)
                {
                    continue;
                }

                _filters[i] = _filters[i].Remove(filter);
                wasRemoved = true;
            }

            return wasRemoved;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void Clear()
    {
        _lock.EnterWriteLock();
        try
        {
            InitializeFilters();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }
}