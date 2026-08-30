using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace OcrAgent.Storage;

/// <summary>
/// Counts SFTP-style operation outcomes and durations; optionally logs periodic rollups.
/// </summary>
public sealed class SftpHealthMonitor : IDisposable
{
    private readonly ILogger? _logger;
    private readonly ConcurrentDictionary<string, HealthMetrics> _metrics = new();
    private readonly Timer? _metricsReportTimer;
    private bool _disposed = false;
    private int _totalOperations = 0;
    private int _successfulOperations = 0;
    private int _failedOperations = 0;

    /// <summary>Starts optional periodic metric logging when interval is positive.</summary>
    /// <param name="logger">Destination for rollup logs; may be null.</param>
    /// <param name="reportIntervalSeconds">Timer period in seconds; zero disables the timer.</param>
    public SftpHealthMonitor(ILogger? logger = null, int reportIntervalSeconds = 300)
    {
        _logger = logger;
        
        if (reportIntervalSeconds > 0)
        {
            var interval = TimeSpan.FromSeconds(reportIntervalSeconds);
            _metricsReportTimer = new Timer(ReportMetrics, null, interval, interval);
        }
    }

    /// <summary>Updates global counters and per-operation aggregates.</summary>
    /// <param name="operationName">Logical operation label.</param>
    /// <param name="success">Whether the call succeeded.</param>
    /// <param name="durationMs">Elapsed milliseconds for successful calls.</param>
    public void RecordOperation(string operationName, bool success, long durationMs)
    {
        if (_disposed) return;
        
        Interlocked.Increment(ref _totalOperations);
        
        if (success)
        {
            Interlocked.Increment(ref _successfulOperations);
        }
        else
        {
            Interlocked.Increment(ref _failedOperations);
        }
        
        var metrics = _metrics.GetOrAdd(operationName, _ => new HealthMetrics { OperationName = operationName });
        
        lock (metrics)
        {
            metrics.TotalCalls++;
            if (success)
            {
                metrics.SuccessfulCalls++;
                metrics.TotalDurationMs += durationMs;
                
                if (durationMs < metrics.MinDurationMs || metrics.MinDurationMs == 0)
                    metrics.MinDurationMs = durationMs;
                
                if (durationMs > metrics.MaxDurationMs)
                    metrics.MaxDurationMs = durationMs;
            }
            else
            {
                metrics.FailedCalls++;
            }
            
            metrics.LastCallTime = DateTime.UtcNow;
        }
    }

    /// <summary>Share of recorded operations that succeeded.</summary>
    /// <returns>Ratio in 0..1; 1.0 when no operations yet.</returns>
    public double GetSuccessRate()
    {
        var total = _totalOperations;
        if (total == 0) return 1.0;
        
        return (double)_successfulOperations / total;
    }

    /// <summary>Snapshot for one operation name, if any calls were recorded.</summary>
    /// <param name="operationName">Label passed to <see cref="RecordOperation"/>.</param>
    /// <returns>Metrics instance or null.</returns>
    public HealthMetrics? GetMetrics(string operationName)
    {
        return _metrics.TryGetValue(operationName, out var metrics) ? metrics : null;
    }

    /// <summary>All per-operation aggregates keyed by name.</summary>
    /// <returns>Live view of the internal dictionary.</returns>
    public IReadOnlyDictionary<string, HealthMetrics> GetAllMetrics()
    {
        return _metrics;
    }

    /// <summary>Clears per-operation data and global counters.</summary>
    public void ResetMetrics()
    {
        _metrics.Clear();
        _totalOperations = 0;
        _successfulOperations = 0;
        _failedOperations = 0;
        _logger?.LogInformation("Health metrics reset");
    }

    private void ReportMetrics(object? state)
    {
        if (_disposed) return;
        
        var total = _totalOperations;
        if (total == 0) return;
        
        var successRate = GetSuccessRate();
        
        _logger?.LogInformation(
            "SFTP Health Metrics - Total: {Total}, Successful: {Success}, Failed: {Failed}, Success Rate: {Rate:P2}",
            total, _successfulOperations, _failedOperations, successRate);
        
        foreach (var (operationName, metrics) in _metrics)
        {
            lock (metrics)
            {
                if (metrics.TotalCalls > 0)
                {
                    var avgDuration = metrics.SuccessfulCalls > 0 
                        ? metrics.TotalDurationMs / metrics.SuccessfulCalls 
                        : 0;
                    
                    var opSuccessRate = (double)metrics.SuccessfulCalls / metrics.TotalCalls;
                    
                    _logger?.LogInformation(
                        "  {Operation}: {Calls} calls, {Rate:P2} success, Avg: {Avg}ms, Min: {Min}ms, Max: {Max}ms",
                        operationName, metrics.TotalCalls, opSuccessRate, avgDuration, 
                        metrics.MinDurationMs, metrics.MaxDurationMs);
                }
            }
        }
    }

    /// <summary>Stops the report timer and emits a final rollup when applicable.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        _metricsReportTimer?.Dispose();
        
        ReportMetrics(null);
        
        _logger?.LogInformation("SftpHealthMonitor disposed");
    }
}
