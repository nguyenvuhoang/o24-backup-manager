using System.Collections.Concurrent;
using BackupManager.Domain.Models;

namespace BackupManager.API.Services;

public sealed class RunCoordinator(BackupPipeline pipeline, ILogger<RunCoordinator> logger)
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _running = new();

    public bool IsRunning(Guid jobId)
    {
        return _running.ContainsKey(jobId);
    }

    public bool TryStart(BackupJob job)
    {
        var source = new CancellationTokenSource();

        if (!_running.TryAdd(job.Id, source))
        {
            source.Dispose();

            logger.LogWarning(
                "Backup job {JobId} ({JobName}) is already running.",
                job.Id,
                job.Name
            );

            return false;
        }

        logger.LogInformation(
            "Backup job {JobId} ({JobName}) accepted for execution.",
            job.Id,
            job.Name
        );

        _ = Task.Run(async () =>
        {
            try
            {
                await pipeline.ExecuteAsync(job, source.Token);
            }
            catch (OperationCanceledException) when (source.IsCancellationRequested)
            {
                logger.LogInformation(
                    "Backup job {JobId} ({JobName}) was cancelled.",
                    job.Id,
                    job.Name
                );
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Unhandled error while executing backup job {JobId} ({JobName}).",
                    job.Id,
                    job.Name
                );
            }
            finally
            {
                _running.TryRemove(job.Id, out _);

                source.Dispose();

                logger.LogInformation(
                    "Backup job {JobId} ({JobName}) left the running set.",
                    job.Id,
                    job.Name
                );
            }
        });

        return true;
    }

    public bool Cancel(Guid jobId)
    {
        if (!_running.TryGetValue(jobId, out var source))
        {
            return false;
        }

        source.Cancel();

        logger.LogInformation("Cancellation requested for backup job {JobId}.", jobId);

        return true;
    }
}
