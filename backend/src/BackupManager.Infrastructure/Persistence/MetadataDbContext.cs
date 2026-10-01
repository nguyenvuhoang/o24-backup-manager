using BackupManager.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using System.Text.Json;

namespace BackupManager.Infrastructure.Persistence;

public sealed class MetadataDbContext(
    DbContextOptions<MetadataDbContext> options
) : DbContext(options)
{
    public DbSet<DatabaseConnection> DatabaseConnections =>
        Set<DatabaseConnection>();

    public DbSet<BackupJobRow> BackupJobs =>
        Set<BackupJobRow>();

    public DbSet<BackupJobDatabaseRow> BackupJobDatabases =>
        Set<BackupJobDatabaseRow>();

    public DbSet<MetadataState> MetadataStates =>
        Set<MetadataState>();

    protected override void OnModelCreating(
        ModelBuilder model)
    {
        var c =
            model.Entity<DatabaseConnection>();

        c.ToTable(
            "DatabaseConnections"
        );

        c.HasKey(
            x => x.Id
        );

        c.Ignore(
            x => x.PasswordSecret
        );

        c.OwnsOne(
            x => x.Settings,
            settings =>
            {
                //
                // Map scalar properties automatically.
                //
                // BackupTransport is a complex object and is
                // mapped separately below as JSON.
                //
                foreach (
                    var property
                    in typeof(DatabaseConnectionSettings)
                        .GetProperties()
                        .Where(
                            x =>
                                x.Name
                                != nameof(
                                    DatabaseConnectionSettings
                                        .BackupTransport
                                )
                        )
                )
                {
                    settings
                        .Property(
                            property.Name
                        )
                        .HasColumnName(
                            property.Name
                        );
                }

                settings
                    .Property(
                        x => x.Name
                    )
                    .IsRequired()
                    .HasMaxLength(
                        200
                    );

                settings
                    .Property(
                        x => x.Provider
                    )
                    .IsRequired();

                settings
                    .Property(
                        x => x.Host
                    )
                    .IsRequired();

                //
                // Store transport settings as JSON.
                //
                // This keeps the schema simple and allows
                // transport configuration to grow without
                // adding a database column for every
                // SSH/rclone option.
                //
                settings
                    .Property(
                        x => x.BackupTransport
                    )
                    .HasColumnName(
                        "BackupTransportJson"
                    )
                    .HasConversion(
                        value =>
                            value == null
                                ? null
                                : JsonSerializer
                                    .Serialize(
                                        value,
                                        (JsonSerializerOptions?)
                                            null
                                    ),

                        value =>
                            string.IsNullOrWhiteSpace(
                                value
                            )
                                ? null
                                : JsonSerializer
                                    .Deserialize<
                                        BackupTransportSettings
                                    >(
                                        value,
                                        (JsonSerializerOptions?)
                                            null
                                    )
                    );
            }
        );

        c.Navigation(
                x => x.Settings
            )
            .IsRequired();

        var j =
            model.Entity<BackupJobRow>();

        j.ToTable(
            "BackupJobs"
        );

        j.HasKey(
            x => x.Id
        );

        j.Property(
                x => x.Name
            )
            .IsRequired()
            .HasMaxLength(
                200
            );

        j.Property(
                x => x.BackupDirectory
            )
            .IsRequired();

        j.Property(
                x => x.SqlServerBackupDirectory
            )
            .IsRequired();

        j.Property(
                x => x.ScheduleTime
            )
            .IsRequired()
            .HasMaxLength(
                5
            );

        j.Property(
                x => x.ScheduleTimeZone
            )
            .IsRequired()
            .HasMaxLength(
                200
            );

        j.HasOne<DatabaseConnection>()
            .WithMany()
            .HasForeignKey(
                x => x.ConnectionId
            )
            .OnDelete(
                DeleteBehavior.Restrict
            );

        j.HasMany(
                x => x.Databases
            )
            .WithOne()
            .HasForeignKey(
                x => x.BackupJobId
            )
            .OnDelete(
                DeleteBehavior.Cascade
            );

        model.Entity<
                BackupJobDatabaseRow
            >()
            .ToTable(
                "BackupJobDatabases"
            )
            .HasKey(
                x => new
                {
                    x.BackupJobId,
                    x.DatabaseName
                }
            );

        model.Entity<
                MetadataState
            >()
            .HasKey(
                x => x.Key
            );
    }
}

public sealed class BackupJobRow
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public Guid? ConnectionId { get; set; }

    public bool Enabled { get; set; }

    //
    // Directory visible to SQL Server itself.
    //
    public string SqlServerBackupDirectory
    {
        get;
        set;
    } =
        "/var/opt/mssql/backup/backup-manager";

    //
    // Directory local to BackupManager.
    //
    public string BackupDirectory
    {
        get;
        set;
    } = "data/backups";

    //
    // Persist schedule explicitly.
    //
    // These values previously existed only on
    // BackupJob and were lost whenever the job
    // was converted to/from BackupJobRow.
    //
    public bool ScheduleEnabled
    {
        get;
        set;
    } = true;

    public string ScheduleTime
    {
        get;
        set;
    } = "03:00";

    public string ScheduleTimeZone
    {
        get;
        set;
    } = "Asia/Vientiane";

    public int RetentionDays
    {
        get;
        set;
    }

    public string? SftpJson
    {
        get;
        set;
    }

    public string? TelegramJson
    {
        get;
        set;
    }

    //
    // Read-only compatibility for imported jobs;
    // new writes use ConnectionId.
    //
    public string? LegacySqlServerJson
    {
        get;
        set;
    }

    public List<BackupJobDatabaseRow>
        Databases
    {
        get;
        set;
    } = [];

    public static BackupJobRow From(
        BackupJob job)
    {
        var schedule =
            job.Schedule
            ?? new BackupSchedule();

        return new BackupJobRow
        {
            Id =
                job.Id,

            Name =
                job.Name,

            ConnectionId =
                job.ConnectionId,

            Enabled =
                job.Enabled,

            SqlServerBackupDirectory =
                string.IsNullOrWhiteSpace(
                    job.SqlServerBackupDirectory
                )
                    ? "/var/opt/mssql/backup/backup-manager"
                    : job.SqlServerBackupDirectory,

            BackupDirectory =
                job.BackupDirectory,

            ScheduleEnabled =
                schedule.Enabled,

            ScheduleTime =
                string.IsNullOrWhiteSpace(
                    schedule.Time
                )
                    ? "03:00"
                    : schedule.Time,

            ScheduleTimeZone =
                string.IsNullOrWhiteSpace(
                    schedule.TimeZone
                )
                    ? "Asia/Vientiane"
                    : schedule.TimeZone,

            RetentionDays =
                job.RetentionDays,

            SftpJson =
                job.Sftp is null
                    ? null
                    : JsonSerializer.Serialize(
                        job.Sftp
                    ),

            TelegramJson =
                job.Telegram is null
                    ? null
                    : JsonSerializer.Serialize(
                        job.Telegram
                    ),

            LegacySqlServerJson =
                job.ConnectionId is null
                && job.SqlServer is not null
                    ? JsonSerializer.Serialize(
                        job.SqlServer
                    )
                    : null,

            Databases =
                job.Databases
                    .Distinct(
                        StringComparer.Ordinal
                    )
                    .Select(
                        databaseName =>
                            new BackupJobDatabaseRow
                            {
                                BackupJobId =
                                    job.Id,

                                DatabaseName =
                                    databaseName
                            }
                    )
                    .ToList()
        };
    }

    public BackupJob ToDomain() =>
        new()
        {
            Id =
                Id,

            Name =
                Name,

            ConnectionId =
                ConnectionId,

            Enabled =
                Enabled,

            SqlServerBackupDirectory =
                string.IsNullOrWhiteSpace(
                    SqlServerBackupDirectory
                )
                    ? "/var/opt/mssql/backup/backup-manager"
                    : SqlServerBackupDirectory,

            BackupDirectory =
                BackupDirectory,

            Schedule =
                new BackupSchedule
                {
                    Enabled =
                        ScheduleEnabled,

                    Time =
                        string.IsNullOrWhiteSpace(
                            ScheduleTime
                        )
                            ? "03:00"
                            : ScheduleTime,

                    TimeZone =
                        string.IsNullOrWhiteSpace(
                            ScheduleTimeZone
                        )
                            ? "Asia/Vientiane"
                            : ScheduleTimeZone
                },

            RetentionDays =
                RetentionDays,

            Sftp =
                SftpJson is null
                    ? null
                    : JsonSerializer
                        .Deserialize<
                            SftpOptions
                        >(
                            SftpJson
                        ),

            Telegram =
                TelegramJson is null
                    ? null
                    : JsonSerializer
                        .Deserialize<
                            TelegramOptions
                        >(
                            TelegramJson
                        ),

            SqlServer =
                LegacySqlServerJson is null
                    ? null
                    : JsonSerializer
                        .Deserialize<
                            SqlServerOptions
                        >(
                            LegacySqlServerJson
                        ),

            Databases =
                Databases
                    .Select(
                        x =>
                            x.DatabaseName
                    )
                    .ToArray()
        };
}

public sealed class BackupJobDatabaseRow
{
    public Guid BackupJobId
    {
        get;
        set;
    }

    public string DatabaseName
    {
        get;
        set;
    } = "";
}

public sealed class MetadataState
{
    public string Key
    {
        get;
        set;
    } = "";
}

public sealed class MetadataDesignFactory
    : IDesignTimeDbContextFactory<
        MetadataDbContext
    >
{
    public MetadataDbContext CreateDbContext(
        string[] args) =>
        new(
            new DbContextOptionsBuilder<
                    MetadataDbContext
                >()
                .UseSqlite(
                    "Data Source=data/backupmanager.db"
                )
                .Options
        );
}