namespace BackupManager.Domain.Models;

public record DatabaseConnectionSettings
{
    public string Name { get; init; } = "";

    public string Provider { get; init; } = "sqlserver";

    public string Host { get; init; } = "";

    public int? Port { get; init; }

    public string? InstanceName { get; init; }

    public bool UseNamedInstanceDiscovery { get; init; }

    public string AuthenticationType { get; init; } = "windows";

    public string? Username { get; init; }

    public string? Database { get; init; }

    public bool Encrypt { get; init; } = true;

    public bool TrustServerCertificate { get; init; }

    public int ConnectionTimeout { get; init; } = 15;

    public int CommandTimeout { get; init; } = 30;

    public string ApplicationName { get; init; } = "BackupManager";

    /// <summary>
    /// Cấu hình transport riêng của server chứa SQL Server.
    ///
    /// Null = chưa cấu hình transport riêng và BackupManager
    /// sẽ fallback về RemoteStorage global để tương thích
    /// với các connection cũ.
    /// </summary>
    public BackupTransportSettings? BackupTransport { get; init; }
}

public sealed record BackupTransportSettings
{
    /// <summary>
    /// Bật upload remote cho connection này.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// SSH host của máy chứa file backup SQL Server.
    /// </summary>
    public string SshHost { get; init; } = "";

    public int SshPort { get; init; } = 22;

    /// <summary>
    /// SSH user trên database server.
    /// </summary>
    public string SshUser { get; init; } = "root";

    /// <summary>
    /// Private key nằm trên máy Windows chạy BackupManager.
    /// Chỉ lưu path, không lưu nội dung private key.
    /// </summary>
    public string SshPrivateKeyPath { get; init; } = "";

    /// <summary>
    /// Tên rclone remote được cấu hình trên database server.
    ///
    /// Ví dụ:
    /// server 138 -> emidev
    /// server 90  -> jitsadmin
    /// </summary>
    public string RcloneRemote { get; init; } = "";

    /// <summary>
    /// Root folder trên Google Drive/rclone remote.
    /// </summary>
    public string RootPath { get; init; } = "DATABASE";

    /// <summary>
    /// Chạy rclone check sau upload.
    /// </summary>
    public bool VerifyAfterUpload { get; init; } = true;

    public int ConnectTimeoutSeconds { get; init; } = 30;

    public int UploadTimeoutMinutes { get; init; } = 240;
}

public sealed record DatabaseConnection
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public DatabaseConnectionSettings Settings { get; init; } = new();

    public string? PasswordSecret { get; init; }

    public string? EncryptedPassword { get; init; }

    public override string ToString() =>
        "DatabaseConnection [credentials redacted]";

    public DateTimeOffset CreatedAtUtc { get; init; } =
        DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; init; } =
        DateTimeOffset.UtcNow;
}