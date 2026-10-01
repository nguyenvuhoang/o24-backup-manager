using BackupManager.Domain.Models;

namespace BackupManager.Application.Connections;

public sealed class DatabaseConnectionService(
    IConfigurationStore store,
    ISecretVault vault,
    IDatabaseProviderResolver resolver
)
{
    private readonly SemaphoreSlim _mutationGate =
        new(1, 1);

    public Task<DatabaseConnectionTestResult> TestAsync(
        DatabaseConnectionOptions options,
        CancellationToken ct
    )
    {
        options = options.Normalize();

        ConnectionValidation.Validate(options);

        return resolver
            .Resolve(options.Provider)
            .TestConnectionAsync(options, ct);
    }

    public Task<DatabaseDiscoveryResult> DiscoverAsync(
        DatabaseConnectionOptions options,
        CancellationToken ct
    )
    {
        options = options.Normalize();

        ConnectionValidation.Validate(options);

        return resolver
            .Resolve(options.Provider)
            .DiscoverDatabasesAsync(options, ct);
    }

    public async Task<IReadOnlyList<DatabaseConnectionView>>
        ListAsync()
    {
        return (await store.GetConnectionsAsync())
            .Select(DatabaseConnectionView.From)
            .ToArray();
    }

    public async Task<DatabaseConnection> GetAsync(
        Guid id
    )
    {
        return (await store.GetConnectionsAsync())
            .FirstOrDefault(c => c.Id == id)
            ?? throw new ConfigurationException(
                "NOT_FOUND",
                "Không tìm thấy kết nối."
            );
    }

    public async Task<DatabaseConnectionOptions>
        ResolveOptionsAsync(Guid id)
    {
        var connection =
            await GetAsync(id);

        return DatabaseConnectionOptions.From(
            connection.Settings,
            vault.DecryptPassword(
                connection.EncryptedPassword
            )
        );
    }

    public async Task<DatabaseConnectionOptions>
        ResolveEditOptionsAsync(
            Guid id,
            DatabaseConnectionOptions input
        )
    {
        var previous =
            await GetAsync(id);

        return input.AuthenticationType == "sqlserver"
            && string.IsNullOrEmpty(input.Password)
                ? input with
                {
                    Password =
                        vault.DecryptPassword(
                            previous.EncryptedPassword
                        )
                }
                : input;
    }

    public async Task<DatabaseConnectionView> SaveAsync(
        Guid? id,
        DatabaseConnectionOptions input,
        CancellationToken ct
    )
    {
        await _mutationGate.WaitAsync(ct);

        try
        {
            var previous =
                id.HasValue
                    ? await GetAsync(id.Value)
                    : null;

            var options =
                input.Normalize();

            if (string.IsNullOrWhiteSpace(
                    options.Name
                ))
            {
                throw new ConfigurationException(
                    "VALIDATION_FAILED",
                    "Tên kết nối là bắt buộc.",
                    new()
                    {
                        ["name"] =
                        [
                            "Nhập tên kết nối."
                        ]
                    }
                );
            }

            if (
                options.AuthenticationType == "sqlserver"
                && string.IsNullOrEmpty(options.Password)
                && previous is not null
            )
            {
                options = options with
                {
                    Password =
                        vault.DecryptPassword(
                            previous.EncryptedPassword
                        )
                };
            }

            //
            // SQL connection CRUD is not responsible
            // for changing BackupTransport.
            //
            // For an existing connection, always
            // preserve the transport currently stored
            // in the database.
            //
            if (previous is not null)
            {
                options = options with
                {
                    BackupTransport =
                        previous.Settings.BackupTransport
                };
            }

            //
            // Persistence is never a way around
            // discovery/connection validation.
            //
            await TestAsync(
                options,
                ct
            );

            await DiscoverAsync(
                options,
                ct
            );

            var connectionId =
                previous?.Id
                ?? Guid.NewGuid();

            var cipher =
                options.AuthenticationType
                    != "sqlserver"
                    ? null
                    : previous is not null
                        && string.IsNullOrEmpty(
                            input.Password
                        )
                            ? previous.EncryptedPassword
                            : vault.EncryptPassword(
                                options.Password!
                            );

            var connection =
                new DatabaseConnection
                {
                    Id = connectionId,

                    Settings =
                        options.ToSettings(),

                    EncryptedPassword =
                        cipher,

                    CreatedAtUtc =
                        previous?.CreatedAtUtc
                        ?? DateTimeOffset.UtcNow,

                    UpdatedAtUtc =
                        DateTimeOffset.UtcNow
                };

            ct.ThrowIfCancellationRequested();

            await store.SaveConnectionAsync(
                connection
            );

            return DatabaseConnectionView.From(
                connection
            );
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<DatabaseConnectionView>
        SaveBackupTransportAsync(
            Guid id,
            BackupTransportSettings transport,
            CancellationToken ct
        )
    {
        await _mutationGate.WaitAsync(ct);

        try
        {
            var previous =
                await GetAsync(id);

            var normalized =
                NormalizeTransport(transport);

            ValidateTransport(normalized);

            var connection =
                previous with
                {
                    Settings =
                        previous.Settings with
                        {
                            BackupTransport =
                                normalized
                        },

                    UpdatedAtUtc =
                        DateTimeOffset.UtcNow
                };

            ct.ThrowIfCancellationRequested();

            await store.SaveConnectionAsync(
                connection
            );

            return DatabaseConnectionView.From(
                connection
            );
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task DeleteAsync(
        Guid id,
        CancellationToken ct
    )
    {
        await _mutationGate.WaitAsync(ct);

        try
        {
            await GetAsync(id);

            if (!await store.DeleteConnectionAsync(id))
            {
                throw new ConfigurationException(
                    "CONNECTION_IN_USE",
                    "Kết nối đang được một backup job sử dụng."
                );
            }
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private static BackupTransportSettings
        NormalizeTransport(
            BackupTransportSettings transport
        )
    {
        return transport with
        {
            SshHost =
                transport.SshHost?.Trim()
                ?? string.Empty,

            SshPort =
                transport.SshPort > 0
                    ? transport.SshPort
                    : 22,

            SshUser =
                string.IsNullOrWhiteSpace(
                    transport.SshUser
                )
                    ? "root"
                    : transport.SshUser.Trim(),

            SshPrivateKeyPath =
                transport.SshPrivateKeyPath?.Trim()
                ?? string.Empty,

            RcloneRemote =
                transport.RcloneRemote?.Trim()
                ?? string.Empty,

            RootPath =
                string.IsNullOrWhiteSpace(
                    transport.RootPath
                )
                    ? "DATABASE"
                    : transport.RootPath
                        .Trim()
                        .Trim('/', '\\'),

            ConnectTimeoutSeconds =
                transport.ConnectTimeoutSeconds > 0
                    ? transport.ConnectTimeoutSeconds
                    : 30,

            UploadTimeoutMinutes =
                transport.UploadTimeoutMinutes > 0
                    ? transport.UploadTimeoutMinutes
                    : 240
        };
    }

    private static void ValidateTransport(
        BackupTransportSettings transport
    )
    {
        //
        // Disabled transport may intentionally
        // contain incomplete configuration.
        //
        if (!transport.Enabled)
            return;

        var errors =
            new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(
                transport.SshHost
            ))
        {
            errors["sshHost"] =
            [
                "SSH host là bắt buộc."
            ];
        }

        if (transport.SshPort is < 1 or > 65535)
        {
            errors["sshPort"] =
            [
                "SSH port phải từ 1 đến 65535."
            ];
        }

        if (string.IsNullOrWhiteSpace(
                transport.SshUser
            ))
        {
            errors["sshUser"] =
            [
                "SSH user là bắt buộc."
            ];
        }

        //
        // SshPrivateKeyPath may be empty here.
        // RemoteBackupStorageService can fall
        // back to the global Windows private
        // key path.
        //

        if (string.IsNullOrWhiteSpace(
                transport.RcloneRemote
            ))
        {
            errors["rcloneRemote"] =
            [
                "Rclone remote là bắt buộc."
            ];
        }

        if (string.IsNullOrWhiteSpace(
                transport.RootPath
            ))
        {
            errors["rootPath"] =
            [
                "Google Drive root path là bắt buộc."
            ];
        }

        if (errors.Count > 0)
        {
            throw new ConfigurationException(
                "VALIDATION_FAILED",
                "Cấu hình backup transport không hợp lệ.",
                errors
            );
        }
    }
}