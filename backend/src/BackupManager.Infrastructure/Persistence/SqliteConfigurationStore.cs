using BackupManager.Application.Connections;
using BackupManager.Domain.Models;
using BackupManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace BackupManager.Infrastructure.Persistence;

public sealed class SqliteConfigurationStore(IDbContextFactory<MetadataDbContext> factory) : IConfigurationStore
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task InitializeAsync(JsonStore legacy, ISecretVault vault)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
        if (await db.MetadataStates.AnyAsync(x => x.Key == "json-import-v1")) return;
        await using var transaction = await db.Database.BeginTransactionAsync();
        foreach (var connection in await legacy.GetConnectionsAsync())
        {
            var password = await vault.ResolveAsync(connection.PasswordSecret);
            var settings = DatabaseConnectionOptions.From(connection.Settings).ToSettings();
            db.DatabaseConnections.Add(connection with
            {
                Settings = settings with { Name = string.IsNullOrWhiteSpace(settings.Name) ? settings.Host : settings.Name },
                PasswordSecret = null, EncryptedPassword = password is null ? null : vault.EncryptPassword(password)
            });
        }
        foreach (var job in await legacy.GetJobsAsync()) db.BackupJobs.Add(BackupJobRow.From(job));
        db.MetadataStates.Add(new() { Key = "json-import-v1" });
        await db.SaveChangesAsync(); await transaction.CommitAsync();
    }
    public async Task<List<DatabaseConnection>> GetConnectionsAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.DatabaseConnections.AsNoTracking().ToListAsync();
    }
    public async Task<List<BackupJob>> GetJobsAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return (await db.BackupJobs.AsNoTracking().Include(x => x.Databases).ToListAsync()).Select(x => x.ToDomain()).ToList();
    }
    public async Task SaveConnectionAsync(DatabaseConnection connection)
    {
        await gate.WaitAsync();
        try
        {
            await using var db = await factory.CreateDbContextAsync();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var previous = await db.DatabaseConnections.AsNoTracking().FirstOrDefaultAsync(x => x.Id == connection.Id);
            if (previous is not null)
            {
                var a = previous.Settings; var b = connection.Settings;
                var port = DatabaseProviders.ResolvePort(a);
                if ((a.Provider != b.Provider || a.Host != b.Host || port != DatabaseProviders.ResolvePort(b)
                    || (port is null && a.InstanceName != b.InstanceName)) && await db.BackupJobs.AnyAsync(x => x.ConnectionId == connection.Id))
                    throw new ConfigurationException("CONNECTION_IN_USE", "Không đổi server của kết nối đang được job sử dụng. Hãy tạo kết nối mới.");
                db.DatabaseConnections.Update(connection);
            }
            else db.DatabaseConnections.Add(connection);
            await db.SaveChangesAsync(); await transaction.CommitAsync();
        }
        finally { gate.Release(); }
    }
    public async Task<bool> DeleteConnectionAsync(Guid id)
    {
        await gate.WaitAsync();
        try
        {
            await using var db = await factory.CreateDbContextAsync();
            await using var transaction = await db.Database.BeginTransactionAsync();
            if (await db.BackupJobs.AnyAsync(x => x.ConnectionId == id)) return false;
            var connection = await db.DatabaseConnections.FindAsync(id);
            if (connection is not null) db.Remove(connection);
            await db.SaveChangesAsync(); await transaction.CommitAsync(); return true;
        }
        finally { gate.Release(); }
    }
    public async Task<BackupJob> SaveJobAsync(BackupJob job, DateTimeOffset? expectedConnectionVersion = null)
    {
        await gate.WaitAsync();
        try
        {
            await using var db = await factory.CreateDbContextAsync();
            await using var transaction = await db.Database.BeginTransactionAsync();
            if (job.ConnectionId is Guid id)
            {
                var connection = await db.DatabaseConnections.FindAsync(id)
                    ?? throw new ConfigurationException("NOT_FOUND", "Kết nối đã bị xóa. Hãy cấu hình lại.");
                if (expectedConnectionVersion.HasValue && connection.UpdatedAtUtc != expectedConnectionVersion)
                    throw new ConfigurationException("CONNECTION_CHANGED", "Kết nối đã thay đổi trong lúc lưu. Hãy kết nối lại.");
            }
            var previous = await db.BackupJobs.Include(x => x.Databases).FirstOrDefaultAsync(x => x.Id == job.Id);
            if (previous is not null) { db.Remove(previous); await db.SaveChangesAsync(); }
            db.BackupJobs.Add(BackupJobRow.From(job));
            await db.SaveChangesAsync(); await transaction.CommitAsync(); return job;
        }
        finally { gate.Release(); }
    }
}
