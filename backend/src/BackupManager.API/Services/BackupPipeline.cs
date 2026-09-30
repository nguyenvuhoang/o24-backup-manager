using System.IO.Compression;
using System.Security.Cryptography;
using BackupManager.Domain.Models;
using BackupManager.Infrastructure.Services;
using BackupManager.Plugins.SqlServer;
using BackupManager.Application.Connections;

namespace BackupManager.API.Services;

public sealed class BackupPipeline(JsonStore store, SqlServerPlugin sql, ProcessRunner runner, SecretVault vault, IHttpClientFactory clients, DatabaseConnectionService connections)
{
    public async Task<BackupRun> ExecuteAsync(BackupJob job, CancellationToken ct)
    {
        var run = new BackupRun { JobId = job.Id, JobName = job.Name, Status = RunStatus.Running, StartedAt = DateTimeOffset.UtcNow };
        await store.SaveRunAsync(run);
        var runDirectory = Path.GetFullPath(Path.Combine(job.BackupDirectory, $"{Sanitize(job.Name)}_{DateTime.Now:yyyyMMdd_HHmmss}"));
        try
        {
            var savedConnection = job.ConnectionId is Guid connectionId ? await connections.GetAsync(connectionId) : null;
            var password = vault.DecryptPassword(savedConnection?.EncryptedPassword);
            var sqlOptions = savedConnection is not null
                ? SqlServerConnectionSettings.ForBackup(savedConnection)
                : job.SqlServer ?? throw new ConfigurationException("CONNECTION_REQUIRED", "Job không có cấu hình kết nối.");
            var backup = AddStage(run, "Backup and verify databases");
            foreach (var database in job.Databases)
            {
                var path = await sql.BackupAsync(sqlOptions, database, runDirectory, ct, password);
                run.Artifacts.Add(await Artifact(path, ct));
                backup.Message = $"{run.Artifacts.Count}/{job.Databases.Length} database(s) verified";
                await store.SaveRunAsync(run);
            }
            Complete(backup, "All database files passed RESTORE VERIFYONLY.");

            var archiveStage = AddStage(run, "Compress and checksum");
            var archive = runDirectory + ".zip";
            ZipFile.CreateFromDirectory(runDirectory, archive, CompressionLevel.SmallestSize, false);
            var archiveArtifact = await Artifact(archive, ct);
            run.Artifacts.Add(archiveArtifact);
            Complete(archiveStage, archiveArtifact.Sha256);

            if (job.Sftp?.Enabled == true)
            {
                var transfer = AddStage(run, "SFTP/SCP transfer");
                var target = $"{job.Sftp.Username}@{job.Sftp.Host}:{job.Sftp.RemotePath.TrimEnd('/')}/{Path.GetFileName(archive)}";
                var args = new List<string> { "-P", job.Sftp.Port.ToString() };
                if (!string.IsNullOrWhiteSpace(job.Sftp.IdentityFile)) args.AddRange(["-i", job.Sftp.IdentityFile]);
                args.AddRange([archive, target]);
                var result = await runner.RunAsync("scp", args, ct);
                if (result.ExitCode != 0) throw new InvalidOperationException("SCP failed: " + result.Error);
                Complete(transfer, target);
            }

            var retention = AddStage(run, "Retention cleanup");
            var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, job.RetentionDays));
            foreach (var file in Directory.EnumerateFiles(Path.GetFullPath(job.BackupDirectory), "*.zip"))
                if (File.GetLastWriteTimeUtc(file) < cutoff) File.Delete(file);
            Complete(retention, $"Removed archives older than {job.RetentionDays} day(s).");
            run.Status = RunStatus.Succeeded;
        }
        catch (OperationCanceledException)
        {
            run.Status = RunStatus.Cancelled;
            run.Error = "Run cancelled.";
        }
        catch (Exception ex)
        {
            run.Status = RunStatus.Failed;
            run.Error = ex.Message;
            var active = run.Stages.LastOrDefault(x => x.Status == RunStatus.Running);
            if (active is not null) { active.Status = RunStatus.Failed; active.Message = ex.Message; active.FinishedAt = DateTimeOffset.UtcNow; }
        }
        finally
        {
            run.FinishedAt = DateTimeOffset.UtcNow;
            await store.SaveRunAsync(run);
            await NotifyAsync(job, run, CancellationToken.None);
        }
        return run;
    }

    private async Task NotifyAsync(BackupJob job, BackupRun run, CancellationToken ct)
    {
        if (job.Telegram?.Enabled != true) return;
        var token = await vault.ResolveAsync(job.Telegram.BotTokenSecret);
        if (string.IsNullOrWhiteSpace(token)) return;
        var message = $"{job.Telegram.Prefix} - {(run.Status == RunStatus.Succeeded ? "✅ SUCCESS" : "❌ " + run.Status)}\nJob: {job.Name}\nFiles: {run.Artifacts.Count}\n{run.Error}";
        await clients.CreateClient().PostAsJsonAsync($"https://api.telegram.org/bot{token}/sendMessage", new { chat_id = job.Telegram.ChatId, text = message }, ct);
    }

    private static RunStage AddStage(BackupRun run, string name)
    {
        var stage = new RunStage(name) { Status = RunStatus.Running, StartedAt = DateTimeOffset.UtcNow };
        run.Stages.Add(stage); return stage;
    }
    private static void Complete(RunStage stage, string message) { stage.Status = RunStatus.Succeeded; stage.Message = message; stage.FinishedAt = DateTimeOffset.UtcNow; }
    private static string Sanitize(string value) => string.Concat(value.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
    private static async Task<BackupArtifact> Artifact(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return new(path, stream.Length, Convert.ToHexString(hash).ToLowerInvariant());
    }
}
