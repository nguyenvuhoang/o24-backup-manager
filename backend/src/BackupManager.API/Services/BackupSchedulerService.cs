using System.Collections.Concurrent;
using System.Text.Json;
using BackupManager.Application.Connections;
using BackupManager.Domain.Models;

namespace BackupManager.API.Services;

public sealed class BackupSchedulerService(
    IConfigurationStore store,
    RunCoordinator coordinator,
    BackupScheduleCalculator scheduleCalculator,
    IHostEnvironment environment,
    ILogger<BackupSchedulerService> logger
) : BackgroundService
{
    private static readonly TimeSpan PollInterval =
        TimeSpan.FromSeconds(20);

    /// <summary>
    /// Maximum amount of time after today's scheduled occurrence
    /// that BackupManager is allowed to automatically catch up.
    ///
    /// Example:
    /// Schedule = 03:00
    /// Service starts = 03:05
    /// => run today's missed 03:00 occurrence.
    ///
    /// We deliberately do not replay old occurrences from
    /// previous days.
    /// </summary>
    private static readonly TimeSpan CatchUpWindow =
        TimeSpan.FromHours(12);

    /// <summary>
    /// Fast duplicate protection for the current process.
    /// Persistent duplicate protection is stored separately
    /// in scheduler-state.json.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, string>
        _lastTriggeredScheduleKeys = new();

    private readonly SemaphoreSlim _stateGate =
        new(1, 1);

    private readonly JsonSerializerOptions _jsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

    private string DataPath =>
        Path.Combine(
            environment.ContentRootPath,
            "data"
        );

    private string SchedulerStatePath =>
        Path.Combine(
            DataPath,
            "scheduler-state.json"
        );

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Backup scheduler started. State file: {StateFile}",
            SchedulerStatePath
        );

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckSchedulesAsync(
                    stoppingToken
                );
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Backup scheduler check failed."
                );
            }

            try
            {
                await Task.Delay(
                    PollInterval,
                    stoppingToken
                );
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        logger.LogInformation(
            "Backup scheduler stopped."
        );
    }

    private async Task CheckSchedulesAsync(
        CancellationToken ct)
    {
        var jobs =
            await store.GetJobsAsync();

        //
        // One timestamp for the entire scheduler pass.
        //
        var nowUtc =
            DateTimeOffset.UtcNow;

        var state =
            await LoadStateAsync(ct);

        foreach (var job in jobs)
        {
            ct.ThrowIfCancellationRequested();

            if (!job.Enabled)
            {
                continue;
            }

            if (job.Schedule?.Enabled != true)
            {
                continue;
            }

            if (
                !scheduleCalculator.TryParseScheduleTime(
                    job.Schedule.Time,
                    out var scheduleTime
                )
            )
            {
                logger.LogWarning(
                    "Job {JobId} ({JobName}) has invalid schedule time '{ScheduleTime}'. Expected HH:mm.",
                    job.Id,
                    job.Name,
                    job.Schedule.Time
                );

                continue;
            }

            TimeZoneInfo timeZone;

            try
            {
                timeZone =
                    scheduleCalculator.ResolveTimeZone(
                        job.Schedule.TimeZone
                    );
            }
            catch (Exception ex)
                when (
                    ex is TimeZoneNotFoundException
                    or InvalidTimeZoneException
                )
            {
                logger.LogWarning(
                    ex,
                    "Job {JobId} ({JobName}) has invalid timezone '{TimeZone}'.",
                    job.Id,
                    job.Name,
                    job.Schedule.TimeZone
                );

                continue;
            }

            var localNow =
                TimeZoneInfo.ConvertTime(
                    nowUtc,
                    timeZone
                );

            //
            // Build today's local scheduled occurrence.
            //
            var scheduledLocal =
                new DateTime(
                    localNow.Year,
                    localNow.Month,
                    localNow.Day,
                    scheduleTime.Hour,
                    scheduleTime.Minute,
                    0,
                    DateTimeKind.Unspecified
                );

            //
            // Handle a timezone where this local wall-clock time
            // does not exist because of DST transition.
            //
            while (timeZone.IsInvalidTime(scheduledLocal))
            {
                scheduledLocal =
                    scheduledLocal.AddMinutes(1);
            }

            var offset =
                ResolveOccurrenceOffset(
                    timeZone,
                    scheduledLocal
                );

            var scheduledFor =
                new DateTimeOffset(
                    scheduledLocal,
                    offset
                );

            var scheduledForUtc =
                scheduledFor.ToUniversalTime();

            //
            // Today's occurrence is still in the future.
            //
            if (nowUtc < scheduledForUtc)
            {
                continue;
            }

            var lateness =
                nowUtc - scheduledForUtc;

            //
            // Do not unexpectedly execute a very old occurrence.
            //
            // Only today's occurrence and only within the configured
            // catch-up window is eligible.
            //
            if (lateness > CatchUpWindow)
            {
                continue;
            }

            var scheduleKey =
                BuildScheduleKey(
                    scheduledFor,
                    job.Schedule.Time,
                    timeZone.Id
                );

            //
            // Fast in-memory duplicate protection.
            //
            if (
                _lastTriggeredScheduleKeys.TryGetValue(
                    job.Id,
                    out var memoryKey
                )
                &&
                string.Equals(
                    memoryKey,
                    scheduleKey,
                    StringComparison.Ordinal
                )
            )
            {
                continue;
            }

            var stateKey =
                job.Id.ToString("D");

            //
            // Persistent duplicate protection.
            //
            // This survives an API / Windows Service restart.
            //
            if (
                state.Jobs.TryGetValue(
                    stateKey,
                    out var persisted
                )
                &&
                string.Equals(
                    persisted.ScheduleKey,
                    scheduleKey,
                    StringComparison.Ordinal
                )
            )
            {
                _lastTriggeredScheduleKeys[job.Id] =
                    scheduleKey;

                continue;
            }

            //
            // If the same job is already queued or running,
            // today's automatic occurrence is considered handled.
            //
            // This prevents a manual backup that overlaps 03:00
            // from immediately being followed by another copy of
            // the same job.
            //
            if (coordinator.IsAccepted(job.Id))
            {
                state.Jobs[stateKey] =
                    CreateJobState(
                        job,
                        scheduleKey,
                        scheduledFor,
                        nowUtc,
                        "SkippedAlreadyActive"
                    );

                await SaveStateAsync(
                    state,
                    ct
                );

                _lastTriggeredScheduleKeys[job.Id] =
                    scheduleKey;

                logger.LogWarning(
                    "Scheduled backup occurrence skipped because job {JobId} ({JobName}) is already queued or running. Scheduled for {ScheduledFor}.",
                    job.Id,
                    job.Name,
                    scheduledFor
                );

                continue;
            }

            //
            // Persist a reservation BEFORE publishing to the
            // in-memory queue.
            //
            // If the process is restarted immediately afterwards,
            // this occurrence will not be queued a second time.
            //
            state.Jobs[stateKey] =
                CreateJobState(
                    job,
                    scheduleKey,
                    scheduledFor,
                    nowUtc,
                    "Reserved"
                );

            await SaveStateAsync(
                state,
                ct
            );

            _lastTriggeredScheduleKeys[job.Id] =
                scheduleKey;

            var queued =
                coordinator.TryEnqueue(job);

            if (queued)
            {
                state.Jobs[stateKey] =
                    CreateJobState(
                        job,
                        scheduleKey,
                        scheduledFor,
                        DateTimeOffset.UtcNow,
                        "Queued"
                    );

                await SaveStateAsync(
                    state,
                    ct
                );

                logger.LogInformation(
                    "Scheduled backup queued. Job {JobId} ({JobName}), scheduled for {ScheduledFor}, local time {LocalTime}, timezone {TimeZone}, lateness {Lateness}, queue length {QueueLength}.",
                    job.Id,
                    job.Name,
                    scheduledFor,
                    localNow,
                    timeZone.Id,
                    lateness,
                    coordinator.QueuedCount
                );
            }
            else
            {
                state.Jobs[stateKey] =
                    CreateJobState(
                        job,
                        scheduleKey,
                        scheduledFor,
                        DateTimeOffset.UtcNow,
                        "QueueRejected"
                    );

                await SaveStateAsync(
                    state,
                    ct
                );

                logger.LogWarning(
                    "Scheduled backup occurrence could not be queued. Job {JobId} ({JobName}), scheduled for {ScheduledFor}. The occurrence remains marked as handled.",
                    job.Id,
                    job.Name,
                    scheduledFor
                );
            }
        }

        CleanupMemoryKeys(jobs);

        await CleanupPersistentStateAsync(
            state,
            jobs,
            ct
        );
    }

    private static TimeSpan ResolveOccurrenceOffset(
        TimeZoneInfo timeZone,
        DateTime localDateTime)
    {
        if (timeZone.IsAmbiguousTime(localDateTime))
        {
            //
            // For an ambiguous DST wall-clock time, use the larger
            // UTC offset, representing the first occurrence.
            //
            return timeZone
                .GetAmbiguousTimeOffsets(localDateTime)
                .Max();
        }

        return timeZone.GetUtcOffset(
            localDateTime
        );
    }

    private static string BuildScheduleKey(
        DateTimeOffset scheduledFor,
        string configuredTime,
        string timeZoneId)
    {
        return
            $"{scheduledFor:yyyy-MM-dd}|"
            + $"{configuredTime.Trim()}|"
            + timeZoneId;
    }

    private static SchedulerJobState CreateJobState(
        BackupJob job,
        string scheduleKey,
        DateTimeOffset scheduledFor,
        DateTimeOffset handledAtUtc,
        string status)
    {
        return new SchedulerJobState
        {
            JobId =
                job.Id,

            JobName =
                job.Name,

            ScheduleKey =
                scheduleKey,

            ScheduledFor =
                scheduledFor,

            HandledAtUtc =
                handledAtUtc,

            Status =
                status
        };
    }

    private void CleanupMemoryKeys(
        IReadOnlyCollection<BackupJob> jobs)
    {
        var activeJobIds =
            jobs
                .Select(x => x.Id)
                .ToHashSet();

        foreach (
            var jobId
            in _lastTriggeredScheduleKeys.Keys
        )
        {
            if (activeJobIds.Contains(jobId))
            {
                continue;
            }

            _lastTriggeredScheduleKeys.TryRemove(
                jobId,
                out _
            );
        }
    }

    private async Task CleanupPersistentStateAsync(
        SchedulerState state,
        IReadOnlyCollection<BackupJob> jobs,
        CancellationToken ct)
    {
        var activeJobIds =
            jobs
                .Select(
                    x => x.Id.ToString("D")
                )
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase
                );

        var changed =
            false;

        foreach (
            var jobId
            in state.Jobs.Keys.ToArray()
        )
        {
            if (activeJobIds.Contains(jobId))
            {
                continue;
            }

            state.Jobs.Remove(jobId);

            changed =
                true;
        }

        if (!changed)
        {
            return;
        }

        await SaveStateAsync(
            state,
            ct
        );
    }

    private async Task<SchedulerState> LoadStateAsync(
        CancellationToken ct)
    {
        await _stateGate.WaitAsync(ct);

        try
        {
            if (!File.Exists(SchedulerStatePath))
            {
                return new SchedulerState();
            }

            await using var stream =
                File.OpenRead(
                    SchedulerStatePath
                );

            return
                await JsonSerializer
                    .DeserializeAsync<SchedulerState>(
                        stream,
                        _jsonOptions,
                        ct
                    )
                ?? new SchedulerState();
        }
        catch (JsonException ex)
        {
            logger.LogError(
                ex,
                "Scheduler state file contains invalid JSON: {StateFile}. Scheduler will continue with empty state.",
                SchedulerStatePath
            );

            return new SchedulerState();
        }
        catch (IOException ex)
        {
            logger.LogError(
                ex,
                "Could not read scheduler state file: {StateFile}.",
                SchedulerStatePath
            );

            return new SchedulerState();
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private async Task SaveStateAsync(
        SchedulerState state,
        CancellationToken ct)
    {
        await _stateGate.WaitAsync(ct);

        try
        {
            Directory.CreateDirectory(
                DataPath
            );

            var temporaryPath =
                SchedulerStatePath + ".tmp";

            await using (
                var stream =
                    File.Create(
                        temporaryPath
                    )
            )
            {
                await JsonSerializer
                    .SerializeAsync(
                        stream,
                        state,
                        _jsonOptions,
                        ct
                    );
            }

            File.Move(
                temporaryPath,
                SchedulerStatePath,
                true
            );
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private sealed class SchedulerState
    {
        public Dictionary<string, SchedulerJobState>
            Jobs { get; init; } =
                new(
                    StringComparer.OrdinalIgnoreCase
                );
    }

    private sealed class SchedulerJobState
    {
        public Guid JobId { get; init; }

        public string JobName { get; init; } =
            string.Empty;

        public string ScheduleKey { get; init; } =
            string.Empty;

        public DateTimeOffset ScheduledFor {
            get;
            init;
        }

        public DateTimeOffset HandledAtUtc {
            get;
            init;
        }

        public string Status { get; init; } =
            string.Empty;
    }
}