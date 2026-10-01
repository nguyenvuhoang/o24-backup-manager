using System.Collections.Concurrent;
using System.Threading.Channels;
using BackupManager.Domain.Models;

namespace BackupManager.API.Services;

public sealed record BackupQueueJobStatus(Guid JobId, string State, int? QueuePosition);

public sealed record BackupQueueSnapshot(
    Guid? CurrentJobId,
    int QueuedCount,
    IReadOnlyList<BackupQueueJobStatus> Jobs
);

public sealed class RunCoordinator : BackgroundService
{
    private readonly BackupPipeline _pipeline;
    private readonly ILogger<RunCoordinator> _logger;

    //
    // Single-reader channel:
    // all manual + scheduled jobs go through the same FIFO queue.
    //
    private readonly Channel<BackupJob> _queue = Channel.CreateUnbounded<BackupJob>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        }
    );

    //
    // Contains both queued and currently-running job IDs.
    //
    // A job ID can exist only once across:
    // - queued
    // - running
    //
    private readonly ConcurrentDictionary<Guid, byte> _acceptedJobs = new();

    //
    // Cancellation tokens exist only for jobs that are
    // actually executing.
    //
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _running = new();

    //
    // Informational FIFO order used by the runtime API.
    //
    // The Channel remains the authoritative execution queue.
    //
    private readonly ConcurrentQueue<Guid> _queuedOrder = new();

    private Guid? _currentJobId;

    public RunCoordinator(BackupPipeline pipeline, ILogger<RunCoordinator> logger)
    {
        _pipeline = pipeline;
        _logger = logger;
    }

    /// <summary>
    /// True only when this job is currently executing.
    /// </summary>
    public bool IsRunning(Guid jobId)
    {
        return _running.ContainsKey(jobId);
    }

    /// <summary>
    /// True when this job is either queued or running.
    /// </summary>
    public bool IsAccepted(Guid jobId)
    {
        return _acceptedJobs.ContainsKey(jobId);
    }

    /// <summary>
    /// True when this job is waiting in the FIFO queue.
    /// </summary>
    public bool IsQueued(Guid jobId)
    {
        return _acceptedJobs.ContainsKey(jobId) && !_running.ContainsKey(jobId);
    }

    /// <summary>
    /// Current job being processed by the single queue worker.
    /// </summary>
    public Guid? CurrentJobId => _currentJobId;

    /// <summary>
    /// Number of jobs currently waiting behind the running job.
    /// </summary>
    public int QueuedCount
    {
        get
        {
            var accepted = _acceptedJobs.Count;

            var running = _running.Count;

            return Math.Max(0, accepted - running);
        }
    }

    /// <summary>
    /// Returns a runtime snapshot suitable for API/UI consumption.
    ///
    /// QueuePosition is 1-based:
    /// 1 = next job that will execute after the current job.
    ///
    /// Running jobs have QueuePosition = null.
    /// Idle jobs are intentionally omitted from Jobs.
    /// </summary>
    public BackupQueueSnapshot GetSnapshot()
    {
        var currentJobId = _currentJobId;

        var queuedIds = _queuedOrder
            .ToArray()
            .Where(jobId => _acceptedJobs.ContainsKey(jobId) && !_running.ContainsKey(jobId))
            .Distinct()
            .ToArray();

        var jobs = new List<BackupQueueJobStatus>();

        if (currentJobId is Guid runningJobId && _running.ContainsKey(runningJobId))
        {
            jobs.Add(new BackupQueueJobStatus(runningJobId, "Running", null));
        }

        for (var index = 0; index < queuedIds.Length; index++)
        {
            jobs.Add(new BackupQueueJobStatus(queuedIds[index], "Queued", index + 1));
        }

        //
        // Defensive fallback:
        //
        // If informational queue state ever becomes temporarily
        // inconsistent, do not hide accepted jobs from the API.
        //
        var representedIds = jobs.Select(x => x.JobId).ToHashSet();

        foreach (var jobId in _acceptedJobs.Keys)
        {
            if (representedIds.Contains(jobId))
            {
                continue;
            }

            if (_running.ContainsKey(jobId))
            {
                jobs.Add(new BackupQueueJobStatus(jobId, "Running", null));

                continue;
            }

            //
            // Position cannot be guaranteed when this defensive
            // path is used, so expose Queued with no position.
            //
            jobs.Add(new BackupQueueJobStatus(jobId, "Queued", null));
        }

        return new BackupQueueSnapshot(currentJobId, queuedIds.Length, jobs);
    }

    /// <summary>
    /// Backward-compatible entry point used by existing API.
    /// </summary>
    public bool TryStart(BackupJob job)
    {
        return TryEnqueue(job);
    }

    public bool TryEnqueue(BackupJob job)
    {
        //
        // Atomic duplicate protection.
        //
        if (!_acceptedJobs.TryAdd(job.Id, 0))
        {
            _logger.LogWarning(
                "Backup job {JobId} ({JobName}) was not queued because it is already queued or running.",
                job.Id,
                job.Name
            );

            return false;
        }

        //
        // Add informational FIFO state before publishing the job
        // to the Channel.
        //
        // This prevents a very fast reader from starting the job
        // before _queuedOrder knows about it.
        //
        _queuedOrder.Enqueue(job.Id);

        if (!_queue.Writer.TryWrite(job))
        {
            _acceptedJobs.TryRemove(job.Id, out _);

            RemoveQueuedJob(job.Id);

            _logger.LogError(
                "Backup job {JobId} ({JobName}) could not be written to the backup queue.",
                job.Id,
                job.Name
            );

            return false;
        }

        _logger.LogInformation(
            "Backup job {JobId} ({JobName}) queued successfully. Queue length: {QueueLength}.",
            job.Id,
            job.Name,
            QueuedCount
        );

        return true;
    }

    /// <summary>
    /// Cancels only a job that is currently executing.
    ///
    /// Queued jobs are intentionally not removed from the Channel.
    /// </summary>
    public bool Cancel(Guid jobId)
    {
        if (!_running.TryGetValue(jobId, out var source))
        {
            _logger.LogWarning(
                "Cancellation requested for backup job {JobId}, but the job is not currently running.",
                jobId
            );

            return false;
        }

        source.Cancel();

        _logger.LogInformation("Cancellation requested for running backup job {JobId}.", jobId);

        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Backup queue worker started.");

        try
        {
            await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                stoppingToken.ThrowIfCancellationRequested();

                RemoveQueuedJob(job.Id);

                await ExecuteJobAsync(job, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
        finally
        {
            _logger.LogInformation("Backup queue worker stopped.");
        }
    }

    private async Task ExecuteJobAsync(BackupJob job, CancellationToken applicationStoppingToken)
    {
        using var jobCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(
            applicationStoppingToken
        );

        if (!_running.TryAdd(job.Id, jobCancellationSource))
        {
            //
            // This should never normally happen because
            // _acceptedJobs already protects the queue.
            //
            _logger.LogError(
                "Backup job {JobId} ({JobName}) reached the queue worker but is already marked as running.",
                job.Id,
                job.Name
            );

            _acceptedJobs.TryRemove(job.Id, out _);

            return;
        }

        _currentJobId = job.Id;

        _logger.LogInformation(
            "Backup job {JobId} ({JobName}) started from queue. Remaining queue length: {QueueLength}.",
            job.Id,
            job.Name,
            QueuedCount
        );

        try
        {
            await _pipeline.ExecuteAsync(job, jobCancellationSource.Token);
        }
        catch (OperationCanceledException) when (jobCancellationSource.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Backup job {JobId} ({JobName}) was cancelled.",
                job.Id,
                job.Name
            );
        }
        catch (Exception ex)
        {
            //
            // BackupPipeline is expected to persist run
            // failure itself.
            //
            // Never allow one failed backup to kill the
            // queue worker.
            //
            _logger.LogError(
                ex,
                "Unhandled error while executing backup job {JobId} ({JobName}). Queue will continue with the next job.",
                job.Id,
                job.Name
            );
        }
        finally
        {
            _running.TryRemove(job.Id, out _);

            _acceptedJobs.TryRemove(job.Id, out _);

            if (_currentJobId == job.Id)
            {
                _currentJobId = null;
            }

            _logger.LogInformation(
                "Backup job {JobId} ({JobName}) completed queue processing. Remaining queue length: {QueueLength}.",
                job.Id,
                job.Name,
                QueuedCount
            );
        }
    }

    private void RemoveQueuedJob(Guid jobId)
    {
        //
        // Normally the requested job is at the head because the
        // Channel and _queuedOrder are written in the same order.
        //
        if (_queuedOrder.TryPeek(out var head) && head == jobId)
        {
            _queuedOrder.TryDequeue(out _);

            return;
        }

        //
        // Defensive reconstruction.
        //
        // ConcurrentQueue cannot remove an arbitrary element,
        // therefore rebuild the informational queue without
        // the requested job.
        //
        var retained = new List<Guid>();

        while (_queuedOrder.TryDequeue(out var queuedJobId))
        {
            if (queuedJobId != jobId && _acceptedJobs.ContainsKey(queuedJobId))
            {
                retained.Add(queuedJobId);
            }
        }

        foreach (var queuedJobId in retained)
        {
            _queuedOrder.Enqueue(queuedJobId);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Backup queue worker is shutting down.");

        _queue.Writer.TryComplete();

        foreach (var item in _running)
        {
            try
            {
                item.Value.Cancel();
            }
            catch
            {
                // Best effort during application shutdown.
            }
        }

        await base.StopAsync(cancellationToken);
    }
}
