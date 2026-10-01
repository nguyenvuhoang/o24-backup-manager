using System.Collections.Concurrent;
using System.Globalization;
using BackupManager.Application.Connections;
using BackupManager.Domain.Models;

namespace BackupManager.API.Services;

public sealed class BackupSchedulerService(
    IConfigurationStore store,
    RunCoordinator coordinator,
    ILogger<BackupSchedulerService> logger
) : BackgroundService
{
    private static readonly TimeSpan PollInterval =
        TimeSpan.FromSeconds(20);

    private readonly ConcurrentDictionary<Guid, string>
        _lastTriggeredScheduleKeys = new();

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Backup scheduler started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckSchedulesAsync(
                    stoppingToken);
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
                    "Backup scheduler check failed.");
            }

            try
            {
                await Task.Delay(
                    PollInterval,
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        logger.LogInformation(
            "Backup scheduler stopped.");
    }

    private async Task CheckSchedulesAsync(
        CancellationToken ct)
    {
        var jobs =
            await store.GetJobsAsync();

        foreach (var job in jobs)
        {
            ct.ThrowIfCancellationRequested();

            if (!job.Enabled)
                continue;

            if (job.Schedule?.Enabled != true)
                continue;

            if (!TryParseScheduleTime(
                    job.Schedule.Time,
                    out var scheduleTime))
            {
                logger.LogWarning(
                    "Job {JobId} ({JobName}) has invalid schedule time '{ScheduleTime}'. Expected HH:mm.",
                    job.Id,
                    job.Name,
                    job.Schedule.Time);

                continue;
            }

            TimeZoneInfo timeZone;

            try
            {
                timeZone =
                    ResolveTimeZone(
                        job.Schedule.TimeZone);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Job {JobId} ({JobName}) has invalid timezone '{TimeZone}'.",
                    job.Id,
                    job.Name,
                    job.Schedule.TimeZone);

                continue;
            }

            var nowUtc =
                DateTimeOffset.UtcNow;

            var localNow =
                TimeZoneInfo.ConvertTime(
                    nowUtc,
                    timeZone);

            if (localNow.Hour != scheduleTime.Hour ||
                localNow.Minute != scheduleTime.Minute)
            {
                continue;
            }

            //
            // Prevent the same schedule occurrence from being
            // processed more than once by the 20-second polling loop.
            //
            var scheduleKey =
                $"{localNow:yyyy-MM-dd}|{scheduleTime:hh\\:mm}|{timeZone.Id}";

            if (_lastTriggeredScheduleKeys.TryGetValue(
                    job.Id,
                    out var previousKey)
                && string.Equals(
                    previousKey,
                    scheduleKey,
                    StringComparison.Ordinal))
            {
                continue;
            }

            //
            // Mark this schedule occurrence before enqueueing.
            //
            // If the same job is already queued/running, this daily
            // occurrence is considered handled and will not be retried
            // repeatedly during the same minute.
            //
            _lastTriggeredScheduleKeys[job.Id] =
                scheduleKey;

            if (coordinator.IsAccepted(job.Id))
            {
                logger.LogWarning(
                    "Scheduled backup occurrence skipped because job {JobId} ({JobName}) is already queued or running. Local time: {LocalTime}.",
                    job.Id,
                    job.Name,
                    localNow);

                continue;
            }

            var queued =
                coordinator.TryEnqueue(job);

            if (queued)
            {
                logger.LogInformation(
                    "Scheduled backup queued. Job {JobId} ({JobName}), scheduled time {ScheduleTime}, local time {LocalTime}, timezone {TimeZone}, queue length {QueueLength}.",
                    job.Id,
                    job.Name,
                    job.Schedule.Time,
                    localNow,
                    timeZone.Id,
                    coordinator.QueuedCount);
            }
            else
            {
                logger.LogWarning(
                    "Scheduled backup could not be queued. Job {JobId} ({JobName}).",
                    job.Id,
                    job.Name);
            }
        }

        CleanupOldKeys(jobs);
    }

    private void CleanupOldKeys(
        IReadOnlyCollection<BackupJob> jobs)
    {
        var activeJobIds =
            jobs.Select(x => x.Id)
                .ToHashSet();

        foreach (var jobId in
                 _lastTriggeredScheduleKeys.Keys)
        {
            if (!activeJobIds.Contains(jobId))
            {
                _lastTriggeredScheduleKeys.TryRemove(
                    jobId,
                    out _);
            }
        }
    }

    private static bool TryParseScheduleTime(
        string? value,
        out TimeOnly time)
    {
        return TimeOnly.TryParseExact(
            value?.Trim(),
            "HH:mm",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out time);
    }

    private static TimeZoneInfo ResolveTimeZone(
        string? timeZoneId)
    {
        var requested =
            string.IsNullOrWhiteSpace(timeZoneId)
                ? "Asia/Vientiane"
                : timeZoneId.Trim();

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(
                requested);
        }
        catch (TimeZoneNotFoundException)
        {
            //
            // Compatibility fallback for older Windows installations.
            //
            if (string.Equals(
                    requested,
                    "Asia/Vientiane",
                    StringComparison.OrdinalIgnoreCase))
            {
                return TimeZoneInfo.FindSystemTimeZoneById(
                    "SE Asia Standard Time");
            }

            throw;
        }
    }
}