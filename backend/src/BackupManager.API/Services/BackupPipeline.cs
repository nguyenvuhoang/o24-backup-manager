using BackupManager.Application.Connections;
using BackupManager.Domain.Models;
using BackupManager.Infrastructure.Services;
using BackupManager.Plugins.SqlServer;

namespace BackupManager.API.Services;

public sealed class BackupPipeline(
    JsonStore store,
    SqlServerPlugin sql,
    SecretVault vault,
    IHttpClientFactory clients,
    DatabaseConnectionService connections,
    RemoteBackupStorageService remoteStorage
)
{
    public async Task<BackupRun> ExecuteAsync(BackupJob job, CancellationToken ct)
    {
        var sqlServerBackupRoot = string.IsNullOrWhiteSpace(job.SqlServerBackupDirectory)
            ? "/var/opt/mssql/backup/backup-manager"
            : job.SqlServerBackupDirectory.Trim();

        var runFolderName = $"{Sanitize(job.Name)}_{DateTime.Now:yyyyMMdd_HHmmss}";

        var sqlServerRunDirectory = CombineServerPath(sqlServerBackupRoot, runFolderName);

        var run = new BackupRun
        {
            JobId = job.Id,
            JobName = job.Name,

            Status = RunStatus.Running,
            CurrentStage = BackupStage.Connecting,

            StartedAt = DateTimeOffset.UtcNow,

            TotalDatabases = job.Databases.Length,

            SqlServerBackupDirectory = sqlServerBackupRoot,

            SqlServerRunDirectory = sqlServerRunDirectory,

            BackupDirectory = job.BackupDirectory,

            Databases = job
                .Databases.Select(database => new DatabaseBackupRun
                {
                    Database = database,
                    Status = RunStatus.Queued,
                    Stage = BackupStage.Queued,
                })
                .ToList(),
        };

        Log(
            run,
            RunLogLevel.Information,
            BackupStage.Connecting,
            null,
            $"Backup run started. SQL Server staging directory: {sqlServerRunDirectory}"
        );

        await store.SaveRunAsync(run);

        try
        {
            //
            // Resolve saved connection.
            //
            var savedConnection = job.ConnectionId is Guid connectionId
                ? await connections.GetAsync(connectionId)
                : null;

            var password = vault.DecryptPassword(savedConnection?.EncryptedPassword);

            var sqlOptions = savedConnection is not null
                ? SqlServerConnectionSettings.ForBackup(savedConnection)
                : job.SqlServer
                    ?? throw new ConfigurationException(
                        "CONNECTION_REQUIRED",
                        "Job không có cấu hình kết nối."
                    );

            Log(
                run,
                RunLogLevel.Information,
                BackupStage.Connecting,
                null,
                $"Connecting to SQL Server: {sqlOptions.Server}"
            );

            await store.SaveRunAsync(run);

            //
            // SQL Server backup + RESTORE VERIFYONLY.
            //
            var backupStage = AddStage(run, "Backup and verify databases");

            run.CurrentStage = BackupStage.BackingUp;

            await store.SaveRunAsync(run);

            foreach (var database in job.Databases)
            {
                ct.ThrowIfCancellationRequested();

                var databaseRun = run.Databases.First(x => x.Database == database);

                run.CurrentDatabase = database;

                run.CurrentStage = BackupStage.BackingUp;

                databaseRun.Status = RunStatus.Running;

                databaseRun.Stage = BackupStage.BackingUp;

                databaseRun.StartedAt = DateTimeOffset.UtcNow;

                Log(
                    run,
                    RunLogLevel.Information,
                    BackupStage.BackingUp,
                    database,
                    $"Starting backup for database '{database}'."
                );

                await store.SaveRunAsync(run);

                try
                {
                    var result = await sql.BackupAsync(
                        sqlOptions,
                        database,
                        sqlServerRunDirectory,
                        ct,
                        password
                    );

                    databaseRun.BackupPath = result.Path;

                    databaseRun.BackupSize = result.Size;

                    databaseRun.SqlOutput = result.Output;

                    databaseRun.Stage = BackupStage.Verifying;

                    run.CurrentStage = BackupStage.Verifying;

                    Log(
                        run,
                        RunLogLevel.Information,
                        BackupStage.Verifying,
                        database,
                        $"BACKUP DATABASE and RESTORE VERIFYONLY completed. Path: {result.Path}"
                    );

                    if (!string.IsNullOrWhiteSpace(result.Output))
                    {
                        Log(
                            run,
                            RunLogLevel.Information,
                            BackupStage.Verifying,
                            database,
                            result.Output
                        );
                    }

                    databaseRun.Status = RunStatus.Succeeded;

                    databaseRun.Stage = BackupStage.Completed;

                    databaseRun.FinishedAt = DateTimeOffset.UtcNow;

                    run.CompletedDatabases++;
                    run.SucceededDatabases++;

                    //
                    // This artifact is server-side on the SQL Server host.
                    //
                    run.Artifacts.Add(new BackupArtifact(result.Path, result.Size ?? 0, ""));

                    backupStage.Message =
                        $"{run.CompletedDatabases}/{run.TotalDatabases} database(s) completed.";

                    Log(
                        run,
                        RunLogLevel.Information,
                        BackupStage.Completed,
                        database,
                        result.Size is > 0
                            ? $"Database '{database}' completed successfully. Backup size: {FormatBytes(result.Size.Value)}."
                            : $"Database '{database}' completed successfully."
                    );

                    await store.SaveRunAsync(run);
                }
                catch (OperationCanceledException)
                {
                    databaseRun.Status = RunStatus.Cancelled;

                    databaseRun.FinishedAt = DateTimeOffset.UtcNow;

                    databaseRun.Error = "Backup cancelled.";

                    throw;
                }
                catch (Exception ex)
                {
                    databaseRun.Status = RunStatus.Failed;

                    databaseRun.Stage = BackupStage.Failed;

                    databaseRun.FinishedAt = DateTimeOffset.UtcNow;

                    databaseRun.Error = ex.Message;

                    run.CompletedDatabases++;
                    run.FailedDatabases++;

                    run.CurrentStage = BackupStage.Failed;

                    backupStage.Status = RunStatus.Failed;

                    backupStage.Message = ex.Message;

                    backupStage.FinishedAt = DateTimeOffset.UtcNow;

                    Log(run, RunLogLevel.Error, BackupStage.Failed, database, ex.Message);

                    await store.SaveRunAsync(run);

                    //
                    // Current policy:
                    // stop immediately on first failed database.
                    //
                    throw;
                }
            }

            Complete(
                backupStage,
                $"All {run.SucceededDatabases} database(s) passed RESTORE VERIFYONLY."
            );

            run.CurrentDatabase = null;

            await store.SaveRunAsync(run);

            //
            // Remote storage.
            //
            if (remoteStorage.Enabled)
            {
                var transferStage = AddStage(run, "Upload backup to Google Drive");

                run.CurrentStage = BackupStage.Transferring;

                Log(
                    run,
                    RunLogLevel.Information,
                    BackupStage.Transferring,
                    null,
                    $"Starting Google Drive upload. Source: {sqlServerRunDirectory}"
                );

                await store.SaveRunAsync(run);

                try
                {
                    var uploadResult = await remoteStorage.UploadAndVerifyAsync(
                        sqlServerRunDirectory,
                        job.Name,
                        run.StartedAt ?? DateTimeOffset.UtcNow,
                        runFolderName,
                        ct
                    );

                    run.RemoteBackupDirectory = uploadResult.Destination;

                    transferStage.Message = $"Upload completed: {uploadResult.Destination}";

                    Log(
                        run,
                        RunLogLevel.Information,
                        BackupStage.Transferring,
                        null,
                        $"Google Drive upload completed. Destination: {uploadResult.Destination}"
                    );

                    if (!string.IsNullOrWhiteSpace(uploadResult.UploadOutput))
                    {
                        Log(
                            run,
                            RunLogLevel.Information,
                            BackupStage.Transferring,
                            null,
                            uploadResult.UploadOutput
                        );
                    }

                    //
                    // UploadAndVerifyAsync performs rclone check
                    // before returning when verification is enabled.
                    //
                    run.CurrentStage = BackupStage.VerifyingUpload;

                    transferStage.Message =
                        $"Upload completed and remote verification passed: {uploadResult.Destination}";

                    if (!string.IsNullOrWhiteSpace(uploadResult.VerificationOutput))
                    {
                        Log(
                            run,
                            RunLogLevel.Information,
                            BackupStage.VerifyingUpload,
                            null,
                            uploadResult.VerificationOutput
                        );
                    }

                    Log(
                        run,
                        RunLogLevel.Information,
                        BackupStage.VerifyingUpload,
                        null,
                        $"Google Drive verification passed. Destination: {uploadResult.Destination}"
                    );

                    Complete(
                        transferStage,
                        $"Google Drive upload and verification completed: {uploadResult.Destination}"
                    );

                    await store.SaveRunAsync(run);
                }
                catch (OperationCanceledException)
                {
                    transferStage.Status = RunStatus.Cancelled;

                    transferStage.Message = "Google Drive upload cancelled.";

                    transferStage.FinishedAt = DateTimeOffset.UtcNow;

                    throw;
                }
                catch (Exception ex)
                {
                    transferStage.Status = RunStatus.Failed;

                    transferStage.Message = ex.Message;

                    transferStage.FinishedAt = DateTimeOffset.UtcNow;

                    run.CurrentStage = BackupStage.Failed;

                    Log(
                        run,
                        RunLogLevel.Error,
                        BackupStage.Failed,
                        null,
                        $"Google Drive upload/verification failed. SQL Server backup files are preserved at '{sqlServerRunDirectory}'. Error: {ex.Message}"
                    );

                    await store.SaveRunAsync(run);

                    throw;
                }
            }
            else
            {
                Log(
                    run,
                    RunLogLevel.Warning,
                    BackupStage.Transferring,
                    null,
                    $"Remote storage is disabled. Backup files remain only on SQL Server host at: {sqlServerRunDirectory}"
                );
            }

            //
            // Only reach Succeeded after remote upload/verification
            // when remote storage is enabled.
            //
            run.CurrentDatabase = null;

            run.CurrentStage = BackupStage.Completed;

            run.Status = RunStatus.Succeeded;

            var finalLocation = !string.IsNullOrWhiteSpace(run.RemoteBackupDirectory)
                ? run.RemoteBackupDirectory
                : sqlServerRunDirectory;

            Log(
                run,
                RunLogLevel.Information,
                BackupStage.Completed,
                null,
                $"Backup run completed successfully. {run.SucceededDatabases}/{run.TotalDatabases} database(s) backed up and verified. Final backup location: {finalLocation}"
            );
        }
        catch (OperationCanceledException)
        {
            run.Status = RunStatus.Cancelled;

            run.CurrentStage = BackupStage.Failed;

            run.Error = "Run cancelled.";

            var active = run.Stages.LastOrDefault(x => x.Status == RunStatus.Running);

            if (active is not null)
            {
                active.Status = RunStatus.Cancelled;

                active.Message = "Run cancelled.";

                active.FinishedAt = DateTimeOffset.UtcNow;
            }

            Log(
                run,
                RunLogLevel.Warning,
                BackupStage.Failed,
                run.CurrentDatabase,
                "Backup run cancelled."
            );
        }
        catch (Exception ex)
        {
            run.Status = RunStatus.Failed;

            run.CurrentStage = BackupStage.Failed;

            run.Error = ex.Message;

            var active = run.Stages.LastOrDefault(x => x.Status == RunStatus.Running);

            if (active is not null)
            {
                active.Status = RunStatus.Failed;

                active.Message = ex.Message;

                active.FinishedAt = DateTimeOffset.UtcNow;
            }

            Log(run, RunLogLevel.Error, BackupStage.Failed, run.CurrentDatabase, ex.Message);
        }
        finally
        {
            run.FinishedAt = DateTimeOffset.UtcNow;

            await store.SaveRunAsync(run);

            try
            {
                await NotifyAsync(job, run, CancellationToken.None);
            }
            catch (Exception ex)
            {
                //
                // Notification failure must never change
                // a successful backup into Failed.
                //
                Log(
                    run,
                    RunLogLevel.Warning,
                    BackupStage.Notifying,
                    null,
                    $"Telegram notification failed: {ex.Message}"
                );

                await store.SaveRunAsync(run);
            }
        }

        return run;
    }

    private async Task NotifyAsync(BackupJob job, BackupRun run, CancellationToken ct)
    {
        if (job.Telegram?.Enabled != true)
            return;

        var token = await vault.ResolveAsync(job.Telegram.BotTokenSecret);

        if (string.IsNullOrWhiteSpace(token))
            return;

        var status = run.Status == RunStatus.Succeeded ? "✅ SUCCESS" : $"❌ {run.Status}";

        var message =
            $"{job.Telegram.Prefix} - {status}\n"
            + $"Job: {job.Name}\n"
            + $"Databases: {run.SucceededDatabases}/{run.TotalDatabases}\n"
            + $"Failed: {run.FailedDatabases}\n"
            + $"SQL path: {run.SqlServerRunDirectory}";

        if (!string.IsNullOrWhiteSpace(run.RemoteBackupDirectory))
        {
            message += $"\nRemote path: {run.RemoteBackupDirectory}";
        }

        if (!string.IsNullOrWhiteSpace(run.Error))
        {
            message += $"\nError: {run.Error}";
        }

        await clients
            .CreateClient()
            .PostAsJsonAsync(
                $"https://api.telegram.org/bot{token}/sendMessage",
                new
                {
                    chat_id = job.Telegram.ChatId,

                    text = message,
                },
                ct
            );
    }

    private static RunStage AddStage(BackupRun run, string name)
    {
        var stage = new RunStage(name)
        {
            Status = RunStatus.Running,

            StartedAt = DateTimeOffset.UtcNow,
        };

        run.Stages.Add(stage);

        return stage;
    }

    private static void Complete(RunStage stage, string message)
    {
        stage.Status = RunStatus.Succeeded;

        stage.Message = message;

        stage.FinishedAt = DateTimeOffset.UtcNow;
    }

    private static void Log(
        BackupRun run,
        RunLogLevel level,
        BackupStage stage,
        string? database,
        string message
    )
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        run.Logs.Add(
            new RunLog
            {
                Timestamp = DateTimeOffset.UtcNow,

                Level = level,

                Stage = stage,

                Database = database,

                Message = message,
            }
        );

        //
        // Prevent runs.json from growing indefinitely.
        //
        if (run.Logs.Count > 2000)
        {
            run.Logs.RemoveRange(0, run.Logs.Count - 2000);
        }
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();

        return string.Concat(value.Select(ch => invalid.Contains(ch) ? '_' : ch));
    }

    private static string CombineServerPath(string directory, string name)
    {
        directory = directory.Trim().TrimEnd('/', '\\');

        if (directory.StartsWith('/'))
            return $"{directory}/{name}";

        return $"{directory}\\{name}";
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];

        double value = bytes;

        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}
