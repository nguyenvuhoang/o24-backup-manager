using System.Text.Json;
using BackupManager.Domain.Models;
using BackupManager.Application.Connections;
using Microsoft.Extensions.Hosting;

namespace BackupManager.Infrastructure.Services;

public sealed class JsonStore(IHostEnvironment environment) : IConfigurationStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly JsonSerializerOptions _json =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

    private string DataPath =>
        Path.Combine(
            environment.ContentRootPath,
            "data"
        );

    private string JobsPath =>
        Path.Combine(
            DataPath,
            "jobs.json"
        );

    private string RunsPath =>
        Path.Combine(
            DataPath,
            "runs.json"
        );

    private string ConnectionsPath =>
        Path.Combine(
            DataPath,
            "connections.json"
        );

    public Task<List<DatabaseConnection>> GetConnectionsAsync() =>
        ReadAsync<DatabaseConnection>(
            ConnectionsPath
        );

    public Task SaveConnectionAsync(
        DatabaseConnection connection
    ) =>
        MutateAsync(
            async () =>
            {
                var items =
                    await ReadCoreAsync<DatabaseConnection>(
                        ConnectionsPath
                    );

                var index =
                    items.FindIndex(
                        x => x.Id == connection.Id
                    );

                if (index >= 0)
                {
                    var previous =
                        items[index].Settings;

                    var current =
                        connection.Settings;

                    var previousPort =
                        DatabaseProviders.ResolvePort(
                            previous
                        );

                    var currentPort =
                        DatabaseProviders.ResolvePort(
                            current
                        );

                    var targetChanged =
                        previous.Provider != current.Provider
                        || previous.Host != current.Host
                        || previousPort != currentPort
                        || (
                            previousPort is null
                            && previous.InstanceName
                                != current.InstanceName
                        );

                    if (
                        targetChanged
                        && (
                            await ReadCoreAsync<BackupJob>(
                                JobsPath
                            )
                        ).Any(
                            j =>
                                j.ConnectionId
                                == connection.Id
                        )
                    )
                    {
                        throw new ConfigurationException(
                            "CONNECTION_IN_USE",
                            "Không đổi server của kết nối đang được job sử dụng. Hãy tạo kết nối mới."
                        );
                    }
                }

                if (index < 0)
                {
                    items.Add(connection);
                }
                else
                {
                    items[index] = connection;
                }

                await WriteCoreAsync(
                    ConnectionsPath,
                    items
                );
            }
        );

    public async Task<bool> DeleteConnectionAsync(
        Guid id
    )
    {
        await _gate.WaitAsync();

        try
        {
            if (
                (
                    await ReadCoreAsync<BackupJob>(
                        JobsPath
                    )
                ).Any(
                    j => j.ConnectionId == id
                )
            )
            {
                return false;
            }

            var items =
                await ReadCoreAsync<DatabaseConnection>(
                    ConnectionsPath
                );

            items.RemoveAll(
                c => c.Id == id
            );

            await WriteCoreAsync(
                ConnectionsPath,
                items
            );

            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<List<BackupJob>> GetJobsAsync() =>
        ReadAsync<BackupJob>(
            JobsPath
        );

    public Task<List<BackupRun>> GetRunsAsync() =>
        ReadAsync<BackupRun>(
            RunsPath
        );

    public async Task<BackupJob> SaveJobAsync(
        BackupJob job,
        DateTimeOffset? expectedConnectionVersion = null
    )
    {
        await MutateAsync(
            async () =>
            {
                if (job.ConnectionId is Guid id)
                {
                    var connection =
                        (
                            await ReadCoreAsync<DatabaseConnection>(
                                ConnectionsPath
                            )
                        ).FirstOrDefault(
                            c => c.Id == id
                        )
                        ?? throw new ConfigurationException(
                            "NOT_FOUND",
                            "Kết nối đã bị xóa. Hãy cấu hình lại."
                        );

                    if (
                        expectedConnectionVersion.HasValue
                        && expectedConnectionVersion
                            != connection.UpdatedAtUtc
                    )
                    {
                        throw new ConfigurationException(
                            "CONNECTION_CHANGED",
                            "Kết nối đã thay đổi trong lúc lưu. Hãy kết nối lại."
                        );
                    }
                }

                var items =
                    await ReadCoreAsync<BackupJob>(
                        JobsPath
                    );

                var index =
                    items.FindIndex(
                        x => x.Id == job.Id
                    );

                if (index < 0)
                {
                    items.Add(job);
                }
                else
                {
                    items[index] = job;
                }

                await WriteCoreAsync(
                    JobsPath,
                    items
                );
            }
        );

        return job;
    }

    public async Task SaveRunAsync(
        BackupRun run
    )
    {
        await MutateAsync(
            async () =>
            {
                var items =
                    await ReadCoreAsync<BackupRun>(
                        RunsPath
                    );

                var index =
                    items.FindIndex(
                        x => x.Id == run.Id
                    );

                if (index < 0)
                {
                    items.Insert(
                        0,
                        run
                    );
                }
                else
                {
                    items[index] = run;
                }

                if (items.Count > 500)
                {
                    items.RemoveRange(
                        500,
                        items.Count - 500
                    );
                }

                await WriteCoreAsync(
                    RunsPath,
                    items
                );
            }
        );
    }

    /// <summary>
    /// Marks runs left in an active state by a previous BackupManager
    /// process as failed/interrupted.
    ///
    /// This must be called once during application startup before new
    /// backup work is accepted.
    /// </summary>
    public async Task<int> RecoverInterruptedRunsAsync()
    {
        await _gate.WaitAsync();

        try
        {
            var runs =
                await ReadCoreAsync<BackupRun>(
                    RunsPath
                );

            if (runs.Count == 0)
            {
                return 0;
            }

            var recovered = 0;

            foreach (var run in runs)
            {
                if (!IsActiveRun(run))
                {
                    continue;
                }

                var now =
                    DateTimeOffset.UtcNow;

                var previousStage =
                    run.CurrentStage;

                var previousDatabase =
                    run.CurrentDatabase;

                const string message =
                    "Backup run was interrupted because the BackupManager service stopped or restarted before the run completed.";

                run.Status =
                    RunStatus.Failed;

                run.CurrentStage =
                    BackupStage.Failed;

                run.Error =
                    message;

                run.FinishedAt =
                    now;

                //
                // Close any database item that was still marked Running.
                //
                foreach (
                    var databaseRun
                    in run.Databases.Where(
                        x => x.Status == RunStatus.Running
                    )
                )
                {
                    databaseRun.Status =
                        RunStatus.Failed;

                    databaseRun.Stage =
                        BackupStage.Failed;

                    databaseRun.Error =
                        message;

                    databaseRun.FinishedAt =
                        now;

                    //
                    // Do not increment CompletedDatabases here.
                    //
                    // The pipeline may have been interrupted at any point,
                    // and changing the historical counters can produce
                    // misleading backup totals.
                    //
                }

                //
                // Close any stage that was still Running.
                //
                foreach (
                    var stage
                    in run.Stages.Where(
                        x => x.Status == RunStatus.Running
                    )
                )
                {
                    stage.Status =
                        RunStatus.Failed;

                    stage.Message =
                        message;

                    stage.FinishedAt =
                        now;
                }

                run.Logs.Add(
                    new RunLog
                    {
                        Timestamp =
                            now,

                        Level =
                            RunLogLevel.Error,

                        Stage =
                            BackupStage.Failed,

                        Database =
                            previousDatabase,

                        Message =
                            $"{message} Last known stage: {previousStage}."
                    }
                );

                if (run.Logs.Count > 2000)
                {
                    run.Logs.RemoveRange(
                        0,
                        run.Logs.Count - 2000
                    );
                }

                recovered++;
            }

            if (recovered > 0)
            {
                await WriteCoreAsync(
                    RunsPath,
                    runs
                );
            }

            return recovered;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool IsActiveRun(
        BackupRun run
    )
    {
        //
        // Status is the authoritative indication that the previous
        // process believed this run was still active.
        //
        // We deliberately do not recover Queued here because the current
        // in-memory queue does not persist queued BackupRun objects.
        //
        return run.Status == RunStatus.Running;
    }

    private async Task<List<T>> ReadAsync<T>(
        string path
    )
    {
        await _gate.WaitAsync();

        try
        {
            return await ReadCoreAsync<T>(
                path
            );
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task MutateAsync(
        Func<Task> action
    )
    {
        await _gate.WaitAsync();

        try
        {
            await action();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<T>> ReadCoreAsync<T>(
        string path
    )
    {
        if (!File.Exists(path))
        {
            return [];
        }

        await using var stream =
            File.OpenRead(path);

        return
            await JsonSerializer.DeserializeAsync<List<T>>(
                stream,
                _json
            )
            ?? [];
    }

    private async Task WriteCoreAsync<T>(
        string path,
        List<T> value
    )
    {
        Directory.CreateDirectory(
            DataPath
        );

        var temporary =
            path + ".tmp";

        await using (
            var stream =
                File.Create(temporary)
        )
        {
            await JsonSerializer.SerializeAsync(
                stream,
                value,
                _json
            );
        }

        File.Move(
            temporary,
            path,
            true
        );
    }
}