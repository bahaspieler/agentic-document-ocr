using System.Collections.Concurrent;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace OcrAgent.Storage;

/// <summary>
/// Holds a bounded set of SFTP clients: warm minimum connections, idle cleanup, optional health checks.
/// </summary>
public sealed class SftpConnectionPool : IDisposable
{
    private readonly SftpStorageConfig _config;
    private readonly ConcurrentBag<PooledConnection> _availableConnections = new();
    private readonly ConcurrentDictionary<int, PooledConnection> _activeConnections = new();
    private readonly SemaphoreSlim _connectionSemaphore;
    private readonly Timer? _idleConnectionCleanupTimer;
    private readonly Timer? _healthCheckTimer;
    private int _totalConnections = 0;
    private bool _disposed = false;

    /// <summary>Creates the pool, pre-opens minimum connections, and starts idle and health timers when configured.</summary>
    /// <param name="config">Connection and pool limits.</param>
    public SftpConnectionPool(SftpStorageConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        
        _connectionSemaphore = new SemaphoreSlim(_config.ConnectionPool.MaxConnections, _config.ConnectionPool.MaxConnections);
        
        InitializeMinimumConnections();
        
        if (_config.ConnectionPool.EnableHealthChecks)
        {
            var healthCheckInterval = TimeSpan.FromSeconds(_config.ConnectionPool.HealthCheckInterval);
            _healthCheckTimer = new Timer(PerformHealthCheck, null, healthCheckInterval, healthCheckInterval);
        }
        
        var idleCheckInterval = TimeSpan.FromSeconds(Math.Max(60, _config.ConnectionPool.ConnectionIdleTimeout / 5));
        _idleConnectionCleanupTimer = new Timer(CleanupIdleConnections, null, idleCheckInterval, idleCheckInterval);
    }

    /// <summary>
    /// Borrows a client: waits on the pool semaphore, takes idle connection or opens a new one, tracks it as active.
    /// </summary>
    /// <param name="cancellationToken">Cancellation for the wait.</param>
    /// <returns>Connected <see cref="SftpClient"/>; must be released with <see cref="ReleaseConnection"/>.</returns>
    public async Task<SftpClient> AcquireConnectionAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Pool lifecycle: acquire slot -> take from idle bag or create -> mark active; ReleaseConnection returns to bag or disposes.
        await _connectionSemaphore.WaitAsync(cancellationToken);

        try
        {
            if (_availableConnections.TryTake(out var pooledConnection))
            {
                if (IsConnectionValid(pooledConnection.Client))
                {
                    pooledConnection.LastUsed = DateTime.UtcNow;
                    _activeConnections.TryAdd(pooledConnection.Id, pooledConnection);
                    return pooledConnection.Client;
                }
                else
                {
                    DisposeConnection(pooledConnection);
                }
            }
            
            var newConnection = await CreateNewConnectionAsync(cancellationToken);
            return newConnection.Client;
        }
        catch
        {
            _connectionSemaphore.Release();
            throw;
        }
    }

    /// <summary>Returns a client to the idle pool or disposes it if invalid; always releases the semaphore slot.</summary>
    /// <param name="client">Client previously acquired from this pool.</param>
    public void ReleaseConnection(SftpClient client)
    {
        if (client == null || _disposed)
        {
            _connectionSemaphore.Release();
            return;
        }

        var connectionId = client.GetHashCode();
        
        if (_activeConnections.TryRemove(connectionId, out var pooledConnection))
        {
            pooledConnection.LastUsed = DateTime.UtcNow;
            
            if (IsConnectionValid(client) && !_disposed)
            {
                _availableConnections.Add(pooledConnection);
            }
            else
            {
                DisposeConnection(pooledConnection);
            }
        }
        
        _connectionSemaphore.Release();
    }

    /// <summary>Acquires a client, runs the operation, then releases the client in a finally block.</summary>
    /// <typeparam name="T">Operation result type.</typeparam>
    /// <param name="operation">Work to run with the borrowed client.</param>
    /// <param name="cancellationToken">Passed to acquire.</param>
    /// <returns>The task result from <paramref name="operation"/>.</returns>
    public async Task<T> ExecuteAsync<T>(Func<SftpClient, Task<T>> operation, CancellationToken cancellationToken = default)
    {
        var client = await AcquireConnectionAsync(cancellationToken);
        try
        {
            return await operation(client);
        }
        finally
        {
            ReleaseConnection(client);
        }
    }

    /// <summary>Acquires a client, awaits the operation, then releases the client.</summary>
    /// <param name="operation">Work to run with the borrowed client.</param>
    /// <param name="cancellationToken">Passed to acquire.</param>
    public async Task ExecuteAsync(Func<SftpClient, Task> operation, CancellationToken cancellationToken = default)
    {
        var client = await AcquireConnectionAsync(cancellationToken);
        try
        {
            await operation(client);
        }
        finally
        {
            ReleaseConnection(client);
        }
    }

    private void InitializeMinimumConnections()
    {
        for (int i = 0; i < _config.ConnectionPool.MinConnections; i++)
        {
            try
            {
                var connection = CreateNewConnectionAsync(CancellationToken.None).GetAwaiter().GetResult();
                _availableConnections.Add(connection);
            }
            catch (Exception)
            {
                // Warm-up is optional; the next acquire can create a connection.
            }
        }
    }

    private async Task<PooledConnection> CreateNewConnectionAsync(CancellationToken cancellationToken)
    {
        var connectionId = Interlocked.Increment(ref _totalConnections);
        
        SftpClient client;
        var authMethod = _config.AuthenticationMethod.ToLowerInvariant();
        
        client = authMethod switch
        {
            "password" => new SftpClient(_config.Host, _config.Port, _config.Username, _config.Password ?? throw new InvalidOperationException("Password is required for password authentication")),
            "privatekey" => CreateClientWithPrivateKey(),
            "agent" => throw new NotImplementedException("SSH agent authentication not yet implemented"),
            _ => throw new ArgumentException($"Unknown authentication method: {_config.AuthenticationMethod}")
        };
        
        client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(_config.Timeouts.ConnectionTimeout);
        client.OperationTimeout = TimeSpan.FromSeconds(_config.Timeouts.OperationTimeout);
        client.BufferSize = (uint)_config.Behavior.BufferSize;
        
        if (_config.ConnectionPool.KeepAliveInterval > 0)
        {
            client.KeepAliveInterval = TimeSpan.FromSeconds(_config.ConnectionPool.KeepAliveInterval);
        }
        
        await Task.Run(() => client.Connect(), cancellationToken);
        
        if (_config.Behavior.CreateRemoteDirectoryIfNotExists)
        {
            EnsureRemoteBasePathExists(client);
        }
        
        return new PooledConnection
        {
            Id = connectionId,
            Client = client,
            Created = DateTime.UtcNow,
            LastUsed = DateTime.UtcNow
        };
    }

    private SftpClient CreateClientWithPrivateKey()
    {
        if (string.IsNullOrWhiteSpace(_config.PrivateKeyPath))
            throw new InvalidOperationException("PrivateKeyPath is required for private key authentication");
        
        if (!File.Exists(_config.PrivateKeyPath))
            throw new FileNotFoundException($"Private key file not found: {_config.PrivateKeyPath}");
        
        PrivateKeyFile keyFile;
        if (!string.IsNullOrWhiteSpace(_config.PrivateKeyPassphrase))
        {
            keyFile = new PrivateKeyFile(_config.PrivateKeyPath, _config.PrivateKeyPassphrase);
        }
        else
        {
            keyFile = new PrivateKeyFile(_config.PrivateKeyPath);
        }
        
        return new SftpClient(_config.Host, _config.Port, _config.Username, keyFile);
    }

    private void EnsureRemoteBasePathExists(SftpClient client)
    {
        var basePath = _config.RemoteBasePath?.Trim().TrimEnd('/').Replace("\\", "/");
        if (string.IsNullOrEmpty(basePath) || basePath == "/") return;
        if (!basePath.StartsWith('/')) basePath = "/" + basePath;

        try
        {
            if (client.Exists(basePath))
                return;
            
            var parts = basePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var currentPath = "";
            
            foreach (var part in parts)
            {
                currentPath += "/" + part;
                try
                {
                    if (!client.Exists(currentPath))
                    {
                        client.CreateDirectory(currentPath);
                    }
                }
                catch (SftpPermissionDeniedException)
                {
                    if (!client.Exists(currentPath))
                        throw;
                }
            }
        }
        catch (Exception)
        {
            // Non-fatal: path already exist or created by the server admin
        }
    }

    private bool IsConnectionValid(SftpClient client)
    {
        if (client == null) return false;
        
        try
        {
            return client.IsConnected && client.ConnectionInfo != null;
        }
        catch
        {
            return false;
        }
    }

    private void PerformHealthCheck(object? state)
    {
        if (_disposed) return;
        
        var connectionsToCheck = _availableConnections.ToArray();
        
        foreach (var pooledConnection in connectionsToCheck)
        {
            if (!IsConnectionValid(pooledConnection.Client))
            {
                if (_availableConnections.TryTake(out var conn) && conn.Id == pooledConnection.Id)
                {
                    DisposeConnection(conn);
                }
            }
        }
    }

    private void CleanupIdleConnections(object? state)
    {
        if (_disposed) return;
        
        var idleTimeout = TimeSpan.FromSeconds(_config.ConnectionPool.ConnectionIdleTimeout);
        var now = DateTime.UtcNow;
        var connectionsToRemove = new List<PooledConnection>();
        
        var availableConnections = _availableConnections.ToArray();
        
        foreach (var connection in availableConnections)
        {
            if ((now - connection.LastUsed) > idleTimeout)
            {
                connectionsToRemove.Add(connection);
            }
        }
        
        var currentCount = _totalConnections - connectionsToRemove.Count;
        if (currentCount < _config.ConnectionPool.MinConnections)
        {
            var keepCount = _config.ConnectionPool.MinConnections - (currentCount - connectionsToRemove.Count);
            connectionsToRemove = connectionsToRemove.OrderByDescending(c => c.LastUsed).Skip(keepCount).ToList();
        }
        
        foreach (var connection in connectionsToRemove)
        {
            if (_availableConnections.TryTake(out var conn) && conn.Id == connection.Id)
            {
                DisposeConnection(conn);
            }
        }
    }

    private void DisposeConnection(PooledConnection connection)
    {
        try
        {
            if (connection.Client.IsConnected)
            {
                connection.Client.Disconnect();
            }
            connection.Client.Dispose();
            Interlocked.Decrement(ref _totalConnections);
        }
        catch (Exception)
        {
            // Connection disposal failure
        }
    }

    /// <summary>Stops timers and disconnects all pooled and active clients.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        
        _healthCheckTimer?.Dispose();
        _idleConnectionCleanupTimer?.Dispose();
        
        foreach (var connection in _availableConnections)
        {
            DisposeConnection(connection);
        }
        
        foreach (var connection in _activeConnections.Values)
        {
            DisposeConnection(connection);
        }
        
        _availableConnections.Clear();
        _activeConnections.Clear();
        
        _connectionSemaphore.Dispose();
    }

    private sealed class PooledConnection
    {
        public int Id { get; init; }
        public required SftpClient Client { get; init; }
        public DateTime Created { get; init; }
        public DateTime LastUsed { get; set; }
    }
}
