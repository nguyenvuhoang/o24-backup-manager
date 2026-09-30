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
}

public sealed record DatabaseConnection
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DatabaseConnectionSettings Settings { get; init; } = new();
    public string? PasswordSecret { get; init; }
    public string? EncryptedPassword { get; init; }
    public override string ToString() => "DatabaseConnection [credentials redacted]";
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
