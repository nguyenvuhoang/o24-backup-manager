using BackupManager.Application.Connections;
using Microsoft.Data.SqlClient;

namespace BackupManager.Plugins.SqlServer;

public sealed class SqlServerDatabaseProvider : IDatabaseProvider
{
    public string Provider => "sqlserver";
    public async Task<DatabaseConnectionTestResult> TestConnectionAsync(DatabaseConnectionOptions options, CancellationToken ct)
    {
        await using var connection = await OpenAsync(options, ct);
        return new(true, connection.DataSource, "Microsoft SQL Server", connection.ServerVersion, "Connection successful");
    }
    public async Task<DatabaseDiscoveryResult> DiscoverDatabasesAsync(DatabaseConnectionOptions options, CancellationToken ct)
    {
        await using var connection = await OpenAsync(options, ct);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = options.CommandTimeout;
        // Catalog visibility depends on the login. NULL size means unavailable, not zero.
        command.CommandText = """
            SELECT d.name, d.state_desc, d.create_date,
                   (SELECT SUM(CONVERT(float, f.size)) * 8.0 / 1024.0
                    FROM sys.master_files f WHERE f.database_id = d.database_id) AS size_mb,
                   HAS_DBACCESS(d.name) AS has_access
            FROM sys.databases d
            WHERE d.database_id > 4
            ORDER BY d.name;
            """;
        try
        {
            await using var reader = await command.ExecuteReaderAsync(ct);
            var databases = new List<DatabaseInfo>();
            while (await reader.ReadAsync(ct))
                databases.Add(new(reader.GetString(0), reader.GetString(1), reader.IsDBNull(3) ? null : reader.GetDouble(3),
                    reader.IsDBNull(2) ? null : reader.GetDateTime(2), !reader.IsDBNull(4) && reader.GetInt32(4) == 1));
            return new(true, new(connection.DataSource, Provider, connection.ServerVersion), databases);
        }
        catch (SqlException) { throw new ConfigurationException("DISCOVERY_FAILED", "Không đọc được metadata. Kiểm tra quyền xem database và command timeout."); }
    }
    private static async Task<SqlConnection> OpenAsync(DatabaseConnectionOptions options, CancellationToken ct)
    {
        ConnectionValidation.Validate(options);
        if (options.AuthenticationType == "windows" && !OperatingSystem.IsWindows())
            throw new ConfigurationException("WINDOWS_AUTH_UNAVAILABLE", "Windows Authentication yêu cầu backend chạy Windows với service identity phù hợp. Backend Linux/Docker này cần SQL Server Authentication.");
        var connection = new SqlConnection(SqlServerConnectionSettings.BuildConnectionString(options));
        try { await connection.OpenAsync(ct); return connection; }
        catch (Exception ex)
        {
            await connection.DisposeAsync();
            if (ex is OperationCanceledException) throw;
            throw new ConfigurationException("CONNECTION_FAILED", "Không kết nối được SQL Server. Kiểm tra host/port, xác thực, chứng chỉ TLS và quyền của service identity.");
        }
    }
}
