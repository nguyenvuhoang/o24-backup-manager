namespace BackupManager.Domain.Models;

public sealed record DatabaseProviderMetadata(string Provider, string DisplayName, int DefaultPort);

public static class DatabaseProviders
{
    public static DatabaseProviderMetadata? Find(string provider) => provider switch
    {
        "sqlserver" => new("sqlserver", "Microsoft SQL Server", 1433),
        "postgresql" => new("postgresql", "PostgreSQL", 5432),
        "oracle" => new("oracle", "Oracle", 1521),
        _ => null
    };

    public static int? ResolvePort(DatabaseConnectionSettings options) => options.Port
        ?? (options.Provider == "sqlserver" && options.UseNamedInstanceDiscovery ? null : Find(options.Provider)?.DefaultPort);
}
