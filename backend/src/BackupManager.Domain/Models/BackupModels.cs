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
    public string BackupDirectory { get; init; } = "data/backups";
    public SftpOptions? Sftp { get; init; }
    public TelegramOptions? Telegram { get; init; }
    public int RetentionDays { get; init; } = 2;
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

public enum RunStatus { Queued, Running, Succeeded, Failed, Cancelled }

public sealed record BackupRun
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid JobId { get; init; }
    public string JobName { get; init; } = "";
    public RunStatus Status { get; set; } = RunStatus.Queued;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public List<RunStage> Stages { get; init; } = [];
    public List<BackupArtifact> Artifacts { get; init; } = [];
    public string? Error { get; set; }
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
