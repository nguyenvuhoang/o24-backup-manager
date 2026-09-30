using BackupManager.Domain.Models;

namespace BackupManager.Application.Connections;

public sealed record DatabaseConnectionOptions : DatabaseConnectionSettings
{
    public string? Password { get; init; }
    public override string ToString() => "DatabaseConnectionOptions [credentials redacted]";
    public DatabaseConnectionOptions Normalize() => this with { Port = DatabaseProviders.ResolvePort(this) };

    // Always copy to a base instance: serializing a derived DTO could persist Password.
    public DatabaseConnectionSettings ToSettings() => new()
    {
        Name = Name.Trim(), Provider = Provider, Host = Host.Trim(), Port = DatabaseProviders.ResolvePort(this),
        UseNamedInstanceDiscovery = UseNamedInstanceDiscovery,
        InstanceName = string.IsNullOrWhiteSpace(InstanceName) ? null : InstanceName.Trim(),
        AuthenticationType = AuthenticationType, Username = AuthenticationType == "windows" ? null : Username?.Trim(),
        Database = string.IsNullOrWhiteSpace(Database) ? null : Database.Trim(), Encrypt = Encrypt,
        TrustServerCertificate = TrustServerCertificate, ConnectionTimeout = ConnectionTimeout,
        CommandTimeout = CommandTimeout, ApplicationName = ApplicationName.Trim()
    };
    public static DatabaseConnectionOptions From(DatabaseConnectionSettings s, string? password = null) => new()
    {
        Name = s.Name, Provider = s.Provider, Host = s.Host, Port = DatabaseProviders.ResolvePort(s), InstanceName = s.InstanceName,
        UseNamedInstanceDiscovery = s.UseNamedInstanceDiscovery,
        AuthenticationType = s.AuthenticationType, Username = s.Username, Password = password, Database = s.Database,
        Encrypt = s.Encrypt, TrustServerCertificate = s.TrustServerCertificate, ConnectionTimeout = s.ConnectionTimeout,
        CommandTimeout = s.CommandTimeout, ApplicationName = s.ApplicationName
    };
}

public sealed record DatabaseInfo(string Name, string Status, double? SizeMb, DateTime? CreatedAt, bool IsAccessible);
public sealed record DatabaseServerInfo(string Name, string Provider, string Version);
public sealed record DatabaseConnectionTestResult(bool Success, string ServerName, string DatabaseEngine, string Version, string Message);
public sealed record DatabaseDiscoveryResult(bool Success, DatabaseServerInfo Server, IReadOnlyList<DatabaseInfo> Databases);
public sealed record DatabaseConnectionView : DatabaseConnectionSettings
{
    public Guid Id { get; init; }
    public bool HasPassword { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
    private DatabaseConnectionView(DatabaseConnectionSettings settings) : base(settings) { }
    public static DatabaseConnectionView From(DatabaseConnection c) => new(DatabaseConnectionOptions.From(c.Settings).ToSettings())
    { Id = c.Id, HasPassword = c.EncryptedPassword is not null, CreatedAtUtc = c.CreatedAtUtc, UpdatedAtUtc = c.UpdatedAtUtc };
}

public interface IDatabaseProvider
{
    string Provider { get; }
    DatabaseProviderMetadata? Metadata => DatabaseProviders.Find(Provider);
    Task<DatabaseConnectionTestResult> TestConnectionAsync(DatabaseConnectionOptions options, CancellationToken ct);
    Task<DatabaseDiscoveryResult> DiscoverDatabasesAsync(DatabaseConnectionOptions options, CancellationToken ct);
}
public interface IDatabaseProviderResolver { IDatabaseProvider Resolve(string provider); }
public sealed class DatabaseProviderResolver(IEnumerable<IDatabaseProvider> providers) : IDatabaseProviderResolver
{
    public IDatabaseProvider Resolve(string provider) => providers.FirstOrDefault(x => x.Provider == provider)
        ?? throw new ConfigurationException("UNSUPPORTED_PROVIDER", "Database provider chưa được hỗ trợ.");
}

public interface IConfigurationStore
{
    Task<List<DatabaseConnection>> GetConnectionsAsync();
    Task SaveConnectionAsync(DatabaseConnection connection);
    Task<bool> DeleteConnectionAsync(Guid id);
    Task<List<BackupJob>> GetJobsAsync();
    Task<BackupJob> SaveJobAsync(BackupJob job, DateTimeOffset? expectedConnectionVersion = null);
}
public interface ISecretVault
{
    string EncryptPassword(string value);
    string? DecryptPassword(string? value);
    Task StoreAsync(string name, string value);
    Task<string?> ResolveAsync(string? name);
    Task DeleteAsync(string name);
}
public sealed class ConfigurationException(string code, string message, Dictionary<string, string[]>? errors = null) : Exception(message)
{
    public string Code { get; } = code;
    public Dictionary<string, string[]>? Errors { get; } = errors;
}
