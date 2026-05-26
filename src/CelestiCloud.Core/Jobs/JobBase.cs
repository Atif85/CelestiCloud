using CelestiCloud.Core.Models;
using System;
using System.Collections.Generic;
using System.Runtime.Remoting;
using System.Text;

namespace CelestiCloud.Core.Jobs;

public abstract class JobBase
{
    public JobConfig Config { get; }
    public bool IsRunning { get; private set; }

    private readonly Lock _lock = new();
    private CancellationTokenSource? _cts;

    protected JobBase(JobConfig config)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// Starts the job execution in the background.
    /// </summary>
    public async Task StartAsync()
    {
        lock (_lock)
        {
            if (IsRunning) return;
            IsRunning = true;
            _cts = new CancellationTokenSource();
        }

        try
        {
            // Execute the actual job implemented by subclasses
            await ExecuteAsync(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Clean exit when cancelled
        }
        finally
        {
            lock (_lock)
            {
                IsRunning = false;
                _cts?.Dispose();
                _cts = null;
            }
        }
    }

    /// <summary>
    /// Gracefully requests the job to stop.
    /// </summary>
    public Task StopAsync()
    {
        lock (_lock)
        {
            _cts?.Cancel();
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// The actual execution logic that must be overridden by concrete jobs.
    /// </summary>
    protected abstract Task ExecuteAsync(CancellationToken cancellationToken);
}
