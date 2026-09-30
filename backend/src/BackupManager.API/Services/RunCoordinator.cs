using System.Collections.Concurrent;
using BackupManager.Domain.Models;
using BackupManager.Infrastructure.Services;
using BackupManager.Plugins.SqlServer;

namespace BackupManager.API.Services;

public sealed class RunCoordinator(BackupPipeline pipeline)
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _running = new();

    public bool IsRunning(Guid jobId) => _running.ContainsKey(jobId);

    public bool TryStart(BackupJob job)
    {
        var source = new CancellationTokenSource();
        if (!_running.TryAdd(job.Id, source)) { source.Dispose(); return false; }
        _ = Task.Run(async () =>
        {
            try { await pipeline.ExecuteAsync(job, source.Token); }
            finally { _running.TryRemove(job.Id, out _); source.Dispose(); }
        });
        return true;
    }

    public bool Cancel(Guid jobId)
    {
        if (!_running.TryGetValue(jobId, out var source)) return false;
        source.Cancel(); return true;
    }
}
