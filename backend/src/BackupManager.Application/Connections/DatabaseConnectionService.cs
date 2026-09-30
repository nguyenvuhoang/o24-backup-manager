using BackupManager.Domain.Models;

namespace BackupManager.Application.Connections;

public sealed class DatabaseConnectionService(IConfigurationStore store, ISecretVault vault, IDatabaseProviderResolver resolver)
{
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    public Task<DatabaseConnectionTestResult> TestAsync(DatabaseConnectionOptions options, CancellationToken ct)
    {
        ConnectionValidation.Validate(options);
        return resolver.Resolve(options.Provider).TestConnectionAsync(options, ct);
    }
    public Task<DatabaseDiscoveryResult> DiscoverAsync(DatabaseConnectionOptions options, CancellationToken ct)
    {
        ConnectionValidation.Validate(options);
        return resolver.Resolve(options.Provider).DiscoverDatabasesAsync(options, ct);
    }
    public async Task<IReadOnlyList<DatabaseConnectionView>> ListAsync() => (await store.GetConnectionsAsync()).Select(DatabaseConnectionView.From).ToArray();
    public async Task<DatabaseConnection> GetAsync(Guid id) => (await store.GetConnectionsAsync()).FirstOrDefault(c => c.Id == id)
        ?? throw new ConfigurationException("NOT_FOUND", "Không tìm thấy kết nối.");
    public async Task<DatabaseConnectionOptions> ResolveOptionsAsync(Guid id)
    {
        var connection = await GetAsync(id);
        return DatabaseConnectionOptions.From(connection.Settings, await vault.ResolveAsync(connection.PasswordSecret));
    }
    public async Task<DatabaseConnectionView> SaveAsync(Guid? id, DatabaseConnectionOptions input, CancellationToken ct)
    {
        await _mutationGate.WaitAsync(ct);
        try
        {
            var previous = id.HasValue ? await GetAsync(id.Value) : null;
            var options = input;
            if (options.AuthenticationType == "sqlserver" && string.IsNullOrEmpty(options.Password) && previous is not null)
                options = options with { Password = await vault.ResolveAsync(previous.PasswordSecret) };
            // Persistence is never a way around discovery/connection validation.
            await DiscoverAsync(options, ct);
            var connectionId = previous?.Id ?? Guid.NewGuid();
            var secret = options.AuthenticationType == "sqlserver" ? $"database-{connectionId:N}-{Guid.NewGuid():N}" : null;
            if (secret is not null) await vault.StoreAsync(secret, options.Password!);
            var connection = new DatabaseConnection
            {
                Id = connectionId, Settings = options.ToSettings(), PasswordSecret = secret,
                CreatedAtUtc = previous?.CreatedAtUtc ?? DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            ct.ThrowIfCancellationRequested();
            await store.SaveConnectionAsync(connection);
            // Old secrets are retained for in-flight backups; no credential is returned to the client.
            return DatabaseConnectionView.From(connection);
        }
        finally { _mutationGate.Release(); }
    }
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await _mutationGate.WaitAsync(ct);
        try
        {
            await GetAsync(id);
            if (!await store.DeleteConnectionAsync(id)) throw new ConfigurationException("CONNECTION_IN_USE", "Kết nối đang được một backup job sử dụng.");
        }
        finally { _mutationGate.Release(); }
    }
}
