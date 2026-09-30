using System.Text;
using BackupManager.Application.Connections;
using BackupManager.Domain.Models;
using BackupManager.Infrastructure.Services;
using Microsoft.Data.SqlClient;

namespace BackupManager.Plugins.SqlServer;

public sealed record SqlServerBackupResult(
    string Database,
    string Path,
    long? Size,
    string Output
);

public sealed class SqlServerPlugin(SecretVault vault)
{
    public async Task<TestResult> DiscoverAsync(
        SqlServerOptions options,
        CancellationToken ct)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(options, ct);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SET NOCOUNT ON;

                SELECT name
                FROM sys.databases
                WHERE state_desc = 'ONLINE'
                  AND name <> 'tempdb'
                ORDER BY name;
                """;

            command.CommandTimeout = ResolveCommandTimeout(options);

            var databases = new List<string>();

            await using var reader = await command.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                if (!reader.IsDBNull(0))
                    databases.Add(reader.GetString(0));
            }

            return new TestResult(
                true,
                $"Found {databases.Count} databases.",
                databases
            );
        }
        catch (Exception ex)
        {
            return new TestResult(
                false,
                FormatException(ex)
            );
        }
    }

    public async Task<SqlServerBackupResult> BackupAsync(
        SqlServerOptions options,
        string database,
        string serverDirectory,
        CancellationToken ct,
        string? resolvedPassword = null)
    {
        if (string.IsNullOrWhiteSpace(database))
            throw new ArgumentException(
                "Database name is required.",
                nameof(database)
            );

        if (string.IsNullOrWhiteSpace(serverDirectory))
            throw new ArgumentException(
                "SQL Server backup directory is required.",
                nameof(serverDirectory)
            );

        var safeDatabase = SanitizeFileName(database);

        var directory = NormalizeServerDirectory(serverDirectory);

        var fileName =
            $"{safeDatabase}_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.bak";

        var backupPath = CombineServerPath(
            directory,
            fileName
        );

        var dbIdentifier =
            database.Replace("]", "]]", StringComparison.Ordinal);

        var sqlPath =
            backupPath.Replace("'", "''", StringComparison.Ordinal);

        var output = new StringBuilder();

        try
        {
            await using var connection =
                await OpenConnectionAsync(
                    options,
                    ct,
                    resolvedPassword
                );

            connection.InfoMessage += (_, args) =>
            {
                foreach (SqlError error in args.Errors)
                {
                    if (output.Length > 0)
                        output.AppendLine();

                    output.Append(error.Message);
                }
            };

            //
            // 1. BACKUP
            //
            await using (var command = connection.CreateCommand())
            {
                command.CommandTimeout =
                    ResolveCommandTimeout(options);

                command.CommandText = $"""
                    BACKUP DATABASE [{dbIdentifier}]
                    TO DISK = N'{sqlPath}'
                    WITH
                        INIT,
                        COMPRESSION,
                        CHECKSUM,
                        STATS = 5;
                    """;

                await command.ExecuteNonQueryAsync(ct);
            }

            //
            // 2. VERIFY
            //
            await using (var command = connection.CreateCommand())
            {
                command.CommandTimeout =
                    ResolveCommandTimeout(options);

                command.CommandText = $"""
                    RESTORE VERIFYONLY
                    FROM DISK = N'{sqlPath}'
                    WITH CHECKSUM;
                    """;

                await command.ExecuteNonQueryAsync(ct);
            }

            //
            // 3. Ask SQL Server OS whether the file exists.
            //
            long? size = null;

            await using (var command = connection.CreateCommand())
            {
                command.CommandTimeout =
                    ResolveCommandTimeout(options);

                command.CommandText = """
                    DECLARE @FileExists int;
                    DECLARE @FileIsDirectory int;
                    DECLARE @ParentDirectoryExists int;

                    EXEC master.dbo.xp_fileexist
                        @Path,
                        @FileExists OUTPUT;

                    SELECT @FileExists;
                    """;

                command.Parameters.Add(
                    new SqlParameter(
                        "@Path",
                        System.Data.SqlDbType.NVarChar,
                        4000
                    )
                    {
                        Value = backupPath
                    }
                );

                var exists =
                    Convert.ToInt32(
                        await command.ExecuteScalarAsync(ct)
                        ?? 0
                    );

                if (exists != 1)
                {
                    throw new InvalidOperationException(
                        $"SQL Server completed BACKUP but the backup file was not found on the SQL Server host: {backupPath}"
                    );
                }
            }

            //
            // 4. Obtain physical file size from SQL Server host.
            //
            try
            {
                await using var sizeCommand =
                    connection.CreateCommand();

                sizeCommand.CommandTimeout =
                    ResolveCommandTimeout(options);

                sizeCommand.CommandText = """
                    SELECT size
                    FROM sys.dm_os_enumerate_filesystem(
                        @Directory,
                        @Pattern
                    )
                    WHERE is_directory = 0;
                    """;

                sizeCommand.Parameters.AddWithValue(
                    "@Directory",
                    directory
                );

                sizeCommand.Parameters.AddWithValue(
                    "@Pattern",
                    fileName
                );

                var value =
                    await sizeCommand.ExecuteScalarAsync(ct);

                if (value is not null &&
                    value != DBNull.Value)
                {
                    size = Convert.ToInt64(value);
                }
            }
            catch
            {
                // File size is informational only.
                // A successful BACKUP + VERIFYONLY is not failed
                // merely because filesystem enumeration is unavailable.
            }

            var messages = output.ToString().Trim();

            if (string.IsNullOrWhiteSpace(messages))
            {
                messages =
                    "BACKUP DATABASE completed successfully. " +
                    "RESTORE VERIFYONLY completed successfully.";
            }

            return new SqlServerBackupResult(
                database,
                backupPath,
                size,
                messages
            );
        }
        catch (SqlException ex)
        {
            throw new InvalidOperationException(
                $"Backup failed for database '{database}'. " +
                $"SQL Server path: {backupPath}. " +
                FormatSqlException(ex),
                ex
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Backup failed for database '{database}'. " +
                $"SQL Server path: {backupPath}. " +
                ex.Message,
                ex
            );
        }
    }

    private async Task<SqlConnection> OpenConnectionAsync(
        SqlServerOptions options,
        CancellationToken ct,
        string? resolvedPassword = null)
    {
        var connectionString =
            await BuildConnectionStringAsync(
                options,
                resolvedPassword
            );

        var connection =
            new SqlConnection(connectionString);

        try
        {
            await connection.OpenAsync(ct);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private async Task<string> BuildConnectionStringAsync(
        SqlServerOptions options,
        string? resolvedPassword)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = options.Server,
            InitialCatalog =
                string.IsNullOrWhiteSpace(options.Database)
                    ? "master"
                    : options.Database.Trim(),

            IntegratedSecurity =
                options.IntegratedSecurity,

            Encrypt =
                options.Encrypt ?? true,

            TrustServerCertificate =
                options.TrustServerCertificate ?? false,

            ConnectTimeout =
                options.ConnectionTimeout ?? 15,

            ApplicationName =
                "BackupManager",

            PersistSecurityInfo = false,
            Pooling = false
        };

        if (!options.IntegratedSecurity)
        {
            var password =
                resolvedPassword
                ?? await vault.ResolveAsync(
                    options.PasswordSecret
                );

            if (string.IsNullOrEmpty(password))
            {
                throw new InvalidOperationException(
                    "SQL password secret is missing."
                );
            }

            builder.UserID =
                options.Username ?? "";

            builder.Password =
                password;
        }

        return builder.ConnectionString;
    }

    private static int ResolveCommandTimeout(
        SqlServerOptions options)
    {
        // BACKUP can legitimately take a long time.
        // 0 means unlimited in SqlCommand.
        return options.CommandTimeout ?? 0;
    }

    private static string NormalizeServerDirectory(
        string directory)
    {
        return directory
            .Trim()
            .TrimEnd('/', '\\');
    }

    private static string CombineServerPath(
        string directory,
        string fileName)
    {
        // Detect Linux/Unix path from configured directory.
        if (directory.StartsWith('/'))
            return $"{directory}/{fileName}";

        return $"{directory}\\{fileName}";
    }

    private static string SanitizeFileName(
        string value)
    {
        var invalid =
            Path.GetInvalidFileNameChars();

        return string.Concat(
            value.Select(
                ch => invalid.Contains(ch)
                    ? '_'
                    : ch
            )
        );
    }

    private static string FormatException(
        Exception ex)
    {
        return ex is SqlException sqlException
            ? FormatSqlException(sqlException)
            : ex.Message;
    }

    private static string FormatSqlException(
        SqlException ex)
    {
        var builder = new StringBuilder();

        foreach (SqlError error in ex.Errors)
        {
            if (builder.Length > 0)
                builder.Append(" | ");

            builder.Append(
                $"SQL {error.Number}, " +
                $"State {error.State}, " +
                $"Class {error.Class}: " +
                error.Message
            );
        }

        return builder.Length == 0
            ? ex.Message
            : builder.ToString();
    }
}