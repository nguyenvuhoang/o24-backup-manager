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
            await using var connection =
                await OpenConnectionAsync(
                    options,
                    ct
                );

            await using var command =
                connection.CreateCommand();

            command.CommandText = """
                SET NOCOUNT ON;

                SELECT name
                FROM sys.databases
                WHERE state_desc = 'ONLINE'
                  AND name <> 'tempdb'
                ORDER BY name;
                """;

            command.CommandTimeout =
                ResolveCommandTimeout(options);

            var databases =
                new List<string>();

            await using var reader =
                await command.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                if (!reader.IsDBNull(0))
                {
                    databases.Add(
                        reader.GetString(0)
                    );
                }
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
        {
            throw new ArgumentException(
                "Database name is required.",
                nameof(database)
            );
        }

        if (string.IsNullOrWhiteSpace(serverDirectory))
        {
            throw new ArgumentException(
                "SQL Server backup directory is required.",
                nameof(serverDirectory)
            );
        }

        var safeDatabase =
            SanitizeFileName(database);

        var directory =
            NormalizeServerDirectory(
                serverDirectory
            );

        var fileName =
            $"{safeDatabase}_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.bak";

        var backupPath =
            CombineServerPath(
                directory,
                fileName
            );

        var dbIdentifier =
            database.Replace(
                "]",
                "]]",
                StringComparison.Ordinal
            );

        var sqlPath =
            backupPath.Replace(
                "'",
                "''",
                StringComparison.Ordinal
            );

        var output =
            new StringBuilder();

        try
        {
            await using var connection =
                await OpenConnectionAsync(
                    options,
                    ct,
                    resolvedPassword
                );

            connection.InfoMessage +=
                (_, args) =>
                {
                    foreach (
                        SqlError error
                        in args.Errors
                    )
                    {
                        if (output.Length > 0)
                        {
                            output.AppendLine();
                        }

                        output.Append(
                            error.Message
                        );
                    }
                };

            //
            // 1. BACKUP DATABASE
            //
            await using (
                var command =
                    connection.CreateCommand()
            )
            {
                command.CommandTimeout =
                    ResolveCommandTimeout(
                        options
                    );

                command.CommandText = $"""
                    BACKUP DATABASE [{dbIdentifier}]
                    TO DISK = N'{sqlPath}'
                    WITH
                        INIT,
                        COMPRESSION,
                        CHECKSUM,
                        STATS = 5;
                    """;

                await command.ExecuteNonQueryAsync(
                    ct
                );
            }

            //
            // 2. RESTORE VERIFYONLY
            //
            await using (
                var command =
                    connection.CreateCommand()
            )
            {
                command.CommandTimeout =
                    ResolveCommandTimeout(
                        options
                    );

                command.CommandText = $"""
                    RESTORE VERIFYONLY
                    FROM DISK = N'{sqlPath}'
                    WITH CHECKSUM;
                    """;

                await command.ExecuteNonQueryAsync(
                    ct
                );
            }

            //
            // 3. Verify the physical backup file exists
            //    on the SQL Server host.
            //
            await using (
                var command =
                    connection.CreateCommand()
            )
            {
                command.CommandTimeout =
                    ResolveCommandTimeout(
                        options
                    );

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
                        await command
                            .ExecuteScalarAsync(ct)
                        ?? 0
                    );

                if (exists != 1)
                {
                    throw new InvalidOperationException(
                        "SQL Server completed BACKUP but " +
                        "the backup file was not found on " +
                        "the SQL Server host: " +
                        backupPath
                    );
                }
            }

            //
            // 4. Obtain backup size from SQL Server's
            //    backup history in msdb.
            //
            //    compressed_backup_size is the actual
            //    size written to the backup media when
            //    backup compression is used.
            //
            //    We match both:
            //      - database name
            //      - exact physical backup path
            //
            //    This avoids relying on OS filesystem
            //    enumeration and works for Linux SQL
            //    Server as well as Windows SQL Server.
            //
            long? size = null;

            try
            {
                await using var sizeCommand =
                    connection.CreateCommand();

                sizeCommand.CommandTimeout =
                    ResolveCommandTimeout(
                        options
                    );

                sizeCommand.CommandText = """
                    SET NOCOUNT ON;

                    SELECT TOP (1)
                        CAST(
                            COALESCE(
                                bs.compressed_backup_size,
                                bs.backup_size
                            )
                            AS bigint
                        ) AS BackupSize
                    FROM msdb.dbo.backupset AS bs
                    INNER JOIN msdb.dbo.backupmediafamily AS bmf
                        ON bmf.media_set_id = bs.media_set_id
                    WHERE bs.database_name = @DatabaseName
                      AND bs.[type] = 'D'
                      AND bmf.physical_device_name = @BackupPath
                    ORDER BY
                        bs.backup_finish_date DESC,
                        bs.backup_set_id DESC;
                    """;

                sizeCommand.Parameters.Add(
                    new SqlParameter(
                        "@DatabaseName",
                        System.Data.SqlDbType.NVarChar,
                        128
                    )
                    {
                        Value = database
                    }
                );

                sizeCommand.Parameters.Add(
                    new SqlParameter(
                        "@BackupPath",
                        System.Data.SqlDbType.NVarChar,
                        4000
                    )
                    {
                        Value = backupPath
                    }
                );

                var value =
                    await sizeCommand
                        .ExecuteScalarAsync(ct);

                if (
                    value is not null
                    && value != DBNull.Value
                )
                {
                    size =
                        Convert.ToInt64(
                            value
                        );
                }
            }
            catch (SqlException ex)
            {
                //
                // Size is informational only.
                //
                // BACKUP + VERIFYONLY + physical file
                // existence have already succeeded.
                //
                // Do not fail the entire backup merely
                // because msdb backup-history metadata
                // cannot be read.
                //
                if (output.Length > 0)
                {
                    output.AppendLine();
                }

                output.Append(
                    "Warning: unable to read backup size " +
                    "from msdb backup history. " +
                    FormatSqlException(ex)
                );
            }

            var messages =
                output
                    .ToString()
                    .Trim();

            if (
                string.IsNullOrWhiteSpace(
                    messages
                )
            )
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
            new SqlConnection(
                connectionString
            );

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
        var builder =
            new SqlConnectionStringBuilder
            {
                DataSource =
                    options.Server,

                InitialCatalog =
                    string.IsNullOrWhiteSpace(
                        options.Database
                    )
                        ? "master"
                        : options.Database.Trim(),

                IntegratedSecurity =
                    options.IntegratedSecurity,

                Encrypt =
                    options.Encrypt
                    ?? true,

                TrustServerCertificate =
                    options.TrustServerCertificate
                    ?? false,

                ConnectTimeout =
                    options.ConnectionTimeout
                    ?? 15,

                ApplicationName =
                    "BackupManager",

                PersistSecurityInfo =
                    false,

                Pooling =
                    false
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
        //
        // BACKUP can legitimately take a long time.
        // 0 means unlimited in SqlCommand.
        //
        return options.CommandTimeout
            ?? 0;
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
        //
        // Detect Linux/Unix path from configured directory.
        //
        if (directory.StartsWith('/'))
        {
            return $"{directory}/{fileName}";
        }

        return $"{directory}\\{fileName}";
    }

    private static string SanitizeFileName(
        string value)
    {
        var invalid =
            Path.GetInvalidFileNameChars();

        return string.Concat(
            value.Select(
                ch =>
                    invalid.Contains(ch)
                        ? '_'
                        : ch
            )
        );
    }

    private static string FormatException(
        Exception ex)
    {
        return ex is SqlException sqlException
            ? FormatSqlException(
                sqlException
            )
            : ex.Message;
    }

    private static string FormatSqlException(
        SqlException ex)
    {
        var builder =
            new StringBuilder();

        foreach (
            SqlError error
            in ex.Errors
        )
        {
            if (builder.Length > 0)
            {
                builder.Append(
                    " | "
                );
            }

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