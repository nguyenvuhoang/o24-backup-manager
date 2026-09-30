namespace BackupManager.Domain.Models;

public sealed record BackupJob
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "New backup job";
    public bool Enabled { get; init; } = true;

    public Guid? ConnectionId { get; init; }

    // Legacy inline configuration remains readable; new jobs use only ConnectionId.
    public SqlServerOptions? SqlServer { get; init; }

    public string[] Databases { get; init; } = [];

    /// <summary>
    /// Directory visible to SQL Server itself.
    /// Linux example: /var/opt/mssql/backup/backup-manager
    /// Windows example: D:\SQLBackups\BackupManager
    /// </summary>
    public string SqlServerBackupDirectory { get; init; } = "/var/opt/mssql/backup/backup-manager";

    /// <summary>
    /// Local BackupManager directory used for downloaded archives/staging.
    /// This path belongs to the machine running BackupManager.
    /// </summary>
    public string BackupDirectory { get; init; } = "data/backups";

    /// <summary>
    /// Automatic schedule for this job.
    /// Default: every day at 03:00 Asia/Vientiane.
    /// </summary>
    public BackupSchedule Schedule { get; init; } = new();

    public SftpOptions? Sftp { get; init; }
    public TelegramOptions? Telegram { get; init; }

    public int RetentionDays { get; init; } = 2;
}

public sealed record BackupSchedule
{
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Daily local time in HH:mm format.
    /// </summary>
    public string Time { get; init; } = "03:00";

    /// <summary>
    /// IANA timezone.
    /// Example: Asia/Vientiane.
    /// </summary>
    public string TimeZone { get; init; } = "Asia/Vientiane";
}

public sealed record SqlServerOptions
{
    public string Server { get; init; } = @"localhost\SQLEXPRESS";
    public bool IntegratedSecurity { get; init; } = true;
    public string? Username { get; init; }
    public string? PasswordSecret { get; init; }

    public bool? Encrypt { get; init; }
    public bool? TrustServerCertificate { get; init; }

    public int? ConnectionTimeout { get; init; }
    public int? CommandTimeout { get; init; }

    public string? Database { get; init; }
}

public sealed record SftpOptions
{
    public bool Enabled { get; init; }
    public string Host { get; init; } = "";
    public int Port { get; init; } = 22;
    public string Username { get; init; } = "";
    public string RemotePath { get; init; } = "/backups";
    public string? IdentityFile { get; init; }
}

public sealed record TelegramOptions
{
    public bool Enabled { get; init; }
    public string ChatId { get; init; } = "";
    public string BotTokenSecret { get; init; } = "";
    public string Prefix { get; init; } = "Backup Manager";
}

public enum RunStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled,
}

public enum BackupStage
{
    Queued,
    Connecting,
    BackingUp,
    Verifying,
    Compressing,
    Transferring,
    VerifyingUpload,
    RetentionCleanup,
    Notifying,
    Completed,
    Failed,
}

public enum RunLogLevel
{
    Information,
    Warning,
    Error,
}

public sealed record BackupRun
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid JobId { get; init; }
    public string JobName { get; init; } = "";

    public RunStatus Status { get; set; } = RunStatus.Queued;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }

    public BackupStage CurrentStage { get; set; } = BackupStage.Queued;
    public string? CurrentDatabase { get; set; }

    public int TotalDatabases { get; set; }
    public int CompletedDatabases { get; set; }
    public int SucceededDatabases { get; set; }
    public int FailedDatabases { get; set; }

    public string? SqlServerBackupDirectory { get; set; }
    public string? SqlServerRunDirectory { get; set; }
    public string? BackupDirectory { get; set; }
    public string? RemoteBackupDirectory { get; set; }

    public List<RunStage> Stages { get; init; } = [];
    public List<DatabaseBackupRun> Databases { get; init; } = [];
    public List<BackupArtifact> Artifacts { get; init; } = [];
    public List<RunLog> Logs { get; init; } = [];

    public string? Error { get; set; }
}

public sealed record DatabaseBackupRun
{
    public string Database { get; init; } = "";

    public RunStatus Status { get; set; } = RunStatus.Queued;
    public BackupStage Stage { get; set; } = BackupStage.Queued;

    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }

    public string? BackupPath { get; set; }

    public long? BackupSize { get; set; }

    public string? SqlOutput { get; set; }
    public string? Error { get; set; }
}

public sealed record RunLog
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public RunLogLevel Level { get; init; } = RunLogLevel.Information;
    public BackupStage Stage { get; init; } = BackupStage.Queued;
    public string? Database { get; init; }
    public string Message { get; init; } = "";
}

public sealed record RunStage(string Name)
{
    public RunStatus Status { get; set; } = RunStatus.Queued;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string Message { get; set; } = "";
}

public sealed record BackupArtifact(string Path, long Size, string Sha256);

public sealed record SecretInput(string Name, string Value);

public sealed record TestResult(bool Success, string Message, IReadOnlyList<string>? Items = null);
