using System.Globalization;
using BackupManager.Domain.Models;

namespace BackupManager.API.Services;

public sealed record BackupScheduleInfo(
    Guid JobId,
    bool Enabled,
    string Time,
    string TimeZone,
    DateTimeOffset? NextRunAt
);

public sealed class BackupScheduleCalculator
{
    public const string DefaultTimeZone =
        "Asia/Vientiane";

    public bool TryParseScheduleTime(
        string? value,
        out TimeOnly time)
    {
        return TimeOnly.TryParseExact(
            value?.Trim(),
            "HH:mm",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out time
        );
    }

    public TimeZoneInfo ResolveTimeZone(
        string? timeZoneId)
    {
        var requested =
            string.IsNullOrWhiteSpace(
                timeZoneId
            )
                ? DefaultTimeZone
                : timeZoneId.Trim();

        try
        {
            return TimeZoneInfo
                .FindSystemTimeZoneById(
                    requested
                );
        }
        catch (TimeZoneNotFoundException)
        {
            //
            // Compatibility fallback for older
            // Windows installations.
            //
            if (string.Equals(
                    requested,
                    DefaultTimeZone,
                    StringComparison.OrdinalIgnoreCase))
            {
                return TimeZoneInfo
                    .FindSystemTimeZoneById(
                        "SE Asia Standard Time"
                    );
            }

            throw;
        }
    }

    public DateTimeOffset? GetNextRunAt(
        BackupJob job,
        DateTimeOffset nowUtc)
    {
        if (!job.Enabled)
            return null;

        if (job.Schedule?.Enabled != true)
            return null;

        if (!TryParseScheduleTime(
                job.Schedule.Time,
                out var scheduleTime))
        {
            return null;
        }

        TimeZoneInfo timeZone;

        try
        {
            timeZone =
                ResolveTimeZone(
                    job.Schedule.TimeZone
                );
        }
        catch (
            TimeZoneNotFoundException)
        {
            return null;
        }
        catch (
            InvalidTimeZoneException)
        {
            return null;
        }

        var localNow =
            TimeZoneInfo.ConvertTime(
                nowUtc,
                timeZone
            );

        //
        // Construct the scheduled wall-clock time
        // for today in the configured timezone.
        //
        var localCandidate =
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
        // If today's occurrence has already
        // happened, move to tomorrow.
        //
        if (localCandidate <=
            localNow.DateTime)
        {
            localCandidate =
                localCandidate.AddDays(1);
        }

        //
        // DST-safe handling.
        //
        // Asia/Vientiane has no DST, but this keeps
        // the scheduler reusable for other zones.
        //
        while (
            timeZone.IsInvalidTime(
                localCandidate
            )
        )
        {
            localCandidate =
                localCandidate.AddMinutes(1);
        }

        TimeSpan offset;

        if (
            timeZone.IsAmbiguousTime(
                localCandidate
            )
        )
        {
            //
            // During a DST fall-back there can be
            // two valid offsets for the same local
            // clock time.
            //
            // Choose the larger offset, which maps
            // to the first occurrence of that local
            // wall-clock time.
            //
            offset =
                timeZone
                    .GetAmbiguousTimeOffsets(
                        localCandidate
                    )
                    .Max();
        }
        else
        {
            offset =
                timeZone.GetUtcOffset(
                    localCandidate
                );
        }

        return new DateTimeOffset(
            localCandidate,
            offset
        );
    }

    public BackupScheduleInfo GetScheduleInfo(
        BackupJob job,
        DateTimeOffset nowUtc)
    {
        var enabled =
            job.Enabled
            && job.Schedule?.Enabled == true;

        var time =
            job.Schedule?.Time?.Trim()
            ?? string.Empty;

        var timeZone =
            string.IsNullOrWhiteSpace(
                job.Schedule?.TimeZone
            )
                ? DefaultTimeZone
                : job.Schedule!.TimeZone.Trim();

        return new BackupScheduleInfo(
            job.Id,
            enabled,
            time,
            timeZone,
            GetNextRunAt(
                job,
                nowUtc
            )
        );
    }
}