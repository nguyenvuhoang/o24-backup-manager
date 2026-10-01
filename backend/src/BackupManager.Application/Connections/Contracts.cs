using BackupManager.Domain.Models;

namespace BackupManager.Application.Connections;

public sealed record DatabaseConnectionOptions : DatabaseConnectionSettings
{
    public string? Password { get; init; }

    public override string ToString() =>
        "DatabaseConnectionOptions [credentials redacted]";

    public DatabaseConnectionOptions Normalize() =>
        this with
        {
            Port = DatabaseProviders.ResolvePort(this)
        };

    // Always copy to a base instance:
    // serializing a derived DTO must never persist Password.
    public DatabaseConnectionSettings ToSettings() => new()
    {
        Name = Name.Trim(),
        Provider = Provider,
        Host = Host.Trim(),
        Port = DatabaseProviders.ResolvePort(this),

        UseNamedInstanceDiscovery = UseNamedInstanceDiscovery,

        InstanceName = string.IsNullOrWhiteSpace(InstanceName)
            ? null
            : InstanceName.Trim(),

        AuthenticationType = AuthenticationType,

        Username = AuthenticationType == "windows"
            ? null
            : Username?.Trim(),

        Database = string.IsNullOrWhiteSpace(Database)
            ? null
            : Database.Trim(),

        Encrypt = Encrypt,
        TrustServerCertificate = TrustServerCertificate,
        ConnectionTimeout = ConnectionTimeout,
        CommandTimeout = CommandTimeout,

        ApplicationName = string.IsNullOrWhiteSpace(ApplicationName)
            ? "BackupManager"
            : ApplicationName.Trim(),

        BackupTransport = BackupTransport
    };

    public static DatabaseConnectionOptions From(
        DatabaseConnectionSettings settings,
        string? password = null
    ) => new()
    {
        Name = settings.Name,
        Provider = settings.Provider,
        Host = settings.Host,
        Port = DatabaseProviders.ResolvePort(settings),
        InstanceName = settings.InstanceName,
        UseNamedInstanceDiscovery = settings.UseNamedInstanceDiscovery,
        AuthenticationType = settings.AuthenticationType,
        Username = settings.Username,
        Password = password,
        Database = settings.Database,
        Encrypt = settings.Encrypt,
        TrustServerCertificate = settings.TrustServerCertificate,
        ConnectionTimeout = settings.ConnectionTimeout,
        CommandTimeout = settings.CommandTimeout,
        ApplicationName = settings.ApplicationName,

        BackupTransport = settings.BackupTransport
    };
}

public sealed record DatabaseInfo(
    string Name,
    string Status,
    double? SizeMb,
    DateTime? CreatedAt,
    bool IsAccessible
);

public sealed record DatabaseServerInfo(
    string Name,
    string Provider,
    string Version
);

public sealed record DatabaseConnectionTestResult(
    bool Success,
    string ServerName,
    string DatabaseEngine,
    string Version,
    string Message
);

public sealed record DatabaseDiscoveryResult(
    bool Success,
    DatabaseServerInfo Server,
    IReadOnlyList<DatabaseInfo> Databases
);

public sealed record DatabaseConnectionView : DatabaseConnectionSettings
{
    public Guid Id { get; init; }

    public bool HasPassword { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    private DatabaseConnectionView(
        DatabaseConnectionSettings settings
    ) : base(settings)
    {
    }

    public static DatabaseConnectionView From(
        DatabaseConnection connection
    ) => new(connection.Settings)
    {
        Id = connection.Id,

        HasPassword =
            connection.EncryptedPassword is not null,

        CreatedAtUtc =
            connection.CreatedAtUtc,

        UpdatedAtUtc =
            connection.UpdatedAtUtc
    };
}

public interface IDatabaseProvider
{
    string Provider { get; }

    DatabaseProviderMetadata? Metadata =>
        DatabaseProviders.Find(Provider);

    Task<DatabaseConnectionTestResult> TestConnectionAsync(
        DatabaseConnectionOptions options,
        CancellationToken ct
    );

    Task<DatabaseDiscoveryResult> DiscoverDatabasesAsync(
        DatabaseConnectionOptions options,
        CancellationToken ct
    );
}

public interface IDatabaseProviderResolver
{
    IDatabaseProvider Resolve(string provider);
}

public sealed class DatabaseProviderResolver(
    IEnumerable<IDatabaseProvider> providers
) : IDatabaseProviderResolver
{
    public IDatabaseProvider Resolve(string provider) =>
        providers.FirstOrDefault(
            x => x.Provider == provider
        )
        ?? throw new ConfigurationException(
            "UNSUPPORTED_PROVIDER",
            "Database provider chưa được hỗ trợ."
        );
}

public interface IConfigurationStore
{
    Task<List<DatabaseConnection>> GetConnectionsAsync();

    Task SaveConnectionAsync(
        DatabaseConnection connection
    );

    Task<bool> DeleteConnectionAsync(Guid id);

    Task<List<BackupJob>> GetJobsAsync();

    Task<BackupJob> SaveJobAsync(
        BackupJob job,
        DateTimeOffset? expectedConnectionVersion = null
    );
}

public interface ISecretVault
{
    string EncryptPassword(string value);

    string? DecryptPassword(string? value);

    Task StoreAsync(
        string name,
        string value
    );

    Task<string?> ResolveAsync(
        string? name
    );

    Task DeleteAsync(string name);
}

public sealed class ConfigurationException(
    string code,
    string message,
    Dictionary<string, string[]>? errors = null
) : Exception(message)
{
    public string Code { get; } = code;

    public Dictionary<string, string[]>? Errors { get; } =
        errors;
}