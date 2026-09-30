using BackupManager.Domain.Models;
using BackupManager.Infrastructure.Persistence;
using BackupManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

internal static class LegacyImportTests
{
    public static async Task RunAsync(Action<bool, string> check, string root)
    {
        var path = Path.Combine(root, "legacy-import"); Directory.CreateDirectory(Path.Combine(path, "data"));
        var environment = new TestEnvironment { ContentRootPath = path };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["BackupManager:Security:MasterKey"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)) }).Build();
        var vault = new SecretVault(environment, configuration);
        var legacy = new JsonStore(environment);
        await vault.StoreAsync("legacy-password", "import-test-secret");
        var connection = new DatabaseConnection { Settings = new() { Host = "legacy.example", Name = "", AuthenticationType = "sqlserver", Username = "test", Port = null }, PasswordSecret = "legacy-password" };
        await legacy.SaveConnectionAsync(connection);
        var job = new BackupJob { Name = "Imported", ConnectionId = connection.Id, Databases = ["Sales"] };
        await legacy.SaveJobAsync(job);
        var original = await File.ReadAllTextAsync(Path.Combine(path, "data", "connections.json"));
        var factory = new TestDbFactory(Path.Combine(path, "data", "backupmanager.db"));
        var store = new SqliteConfigurationStore(factory);
        await store.InitializeAsync(legacy, vault);
        var imported = (await store.GetConnectionsAsync()).Single();
        check(imported.Id == connection.Id && imported.Settings.Port == 1433 && imported.Settings.Name == "legacy.example", "legacy import preserves ID and normalizes missing name/port");
        check(imported.PasswordSecret is null && vault.DecryptPassword(imported.EncryptedPassword) == "import-test-secret", "legacy secret re-encrypted into SQLite without JSON reference");
        check((await store.GetJobsAsync()).Single().ConnectionId == connection.Id, "legacy import preserves job foreign key and databases");
        check(await File.ReadAllTextAsync(Path.Combine(path, "data", "connections.json")) == original, "legacy source retained unchanged for recovery");
        await using (var db = factory.CreateDbContext()) { await db.BackupJobs.ExecuteDeleteAsync(); await db.DatabaseConnections.ExecuteDeleteAsync(); }
        await new SqliteConfigurationStore(factory).InitializeAsync(legacy, vault);
        check((await store.GetConnectionsAsync()).Count == 0 && (await store.GetJobsAsync()).Count == 0, "one-time import marker prevents deleted resources reappearing on restart");
    }
}
