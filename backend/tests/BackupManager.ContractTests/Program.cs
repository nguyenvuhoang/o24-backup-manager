using BackupManager.Application.Connections;
using BackupManager.Domain.Models;
using BackupManager.Plugins.SqlServer;
using Microsoft.Data.SqlClient;
using BackupManager.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using System.Text.Json;

var passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); passed++; Console.WriteLine($"PASS {name}"); }
var options = new DatabaseConnectionOptions { Host = "db.example", InstanceName = "REPORTS" };
Check(SqlServerConnectionSettings.ResolveDataSource(options) == @"db.example\REPORTS", "named instance");
Check(SqlServerConnectionSettings.ResolveDataSource(options with { Port = 1444 }) == "tcp:db.example,1444", "explicit port overrides instance");
Check(SqlServerConnectionSettings.ResolveDataSource(options with { InstanceName = null }) == "db.example", "default instance");
var sqlOptions = options with { AuthenticationType = "sqlserver", Username = "user", Password = "secret;Password=other" };
var builder = new SqlConnectionStringBuilder(SqlServerConnectionSettings.BuildConnectionString(sqlOptions));
Check(builder.Password == sqlOptions.Password && !builder.IntegratedSecurity, "builder escapes credentials");
Check(builder.InitialCatalog == "master" && builder.ConnectTimeout == 15, "optional database and bounded timeout");
Check(ConnectionValidation.GetErrors(options with { Host = "" }).ContainsKey("host"), "required host");
Check(ConnectionValidation.GetErrors(options with { Port = 0 }).ContainsKey("port"), "invalid port");
Check(ConnectionValidation.GetErrors(options with { ConnectionTimeout = 0 }).ContainsKey("connectionTimeout"), "no unlimited connection timeout");
Check(ConnectionValidation.GetErrors(sqlOptions with { Password = null }).ContainsKey("password"), "SQL password required");
Check(!ConnectionValidation.GetErrors(options with { Password = null }).ContainsKey("password"), "Windows needs no SQL password");
Check(ConnectionValidation.GetErrors(options with { Host = "db;Password=bad" }).ContainsKey("host"), "reject embedded connection strings");
var directory = Path.Combine(Path.GetTempPath(), "BackupManager-contracts-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var environment = new TestEnvironment { ContentRootPath = directory };
var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["BackupManager:MasterKey"] = new string('k', 40) }).Build();
var vault = new SecretVault(environment, configuration);
var store = new JsonStore(environment);
var provider = new TestProvider();
var service = new DatabaseConnectionService(store, vault, new DatabaseProviderResolver([provider]));
await service.TestAsync(sqlOptions, default);
await service.DiscoverAsync(sqlOptions, default);
Check((await store.GetConnectionsAsync()).Count == 0, "test/discover never persist");
provider.Fail = true;
try { await service.SaveAsync(null, sqlOptions, default); throw new Exception("Expected failure"); }
catch (ConfigurationException e) { Check(e.Code == "CONNECTION_FAILED", "failed discovery blocks persistence"); }
Check((await store.GetConnectionsAsync()).Count == 0, "no saved connection after failure");
provider.Fail = false;
var saved = await service.SaveAsync(null, sqlOptions, default);
var json = JsonSerializer.Serialize(saved);
Check(!json.Contains("secret;") && !json.Contains("passwordSecret", StringComparison.OrdinalIgnoreCase) && !json.Contains("encryptedPassword", StringComparison.OrdinalIgnoreCase), "response contains no credentials or secret reference");
var persisted = await File.ReadAllTextAsync(Path.Combine(directory, "data", "connections.json"));
Check(!persisted.Contains("secret;"), "connection file has no plaintext password");
Check(!(await File.ReadAllTextAsync(Path.Combine(directory, "data", "secrets.json"))).Contains("secret;"), "vault file contains only ciphertext");
Check((await service.ResolveOptionsAsync(saved.Id)).Password == sqlOptions.Password, "vault roundtrip");
await service.SaveAsync(saved.Id, sqlOptions with { Password = null }, default);
Check((await service.ResolveOptionsAsync(saved.Id)).Password == sqlOptions.Password, "null update preserves password");
await service.SaveAsync(saved.Id, sqlOptions with { Password = "" }, default);
Check((await service.ResolveOptionsAsync(saved.Id)).Password == sqlOptions.Password, "empty update preserves password");
await service.SaveAsync(saved.Id, sqlOptions with { Password = "replacement" }, default);
Check((await service.ResolveOptionsAsync(saved.Id)).Password == "replacement", "new password replaces secret");
var jobs = new BackupJobService(store, service);
try { await jobs.SaveAsync(new BackupJob { SqlServer = new() { Server = "new-inline" }, Databases = ["Sales"] }, default); throw new Exception("New inline job was accepted"); }
catch (ConfigurationException e) { Check(e.Code == "VALIDATION_FAILED", "new inline jobs cannot bypass discovery"); }
var job = new BackupJob { Name = "Test", ConnectionId = saved.Id, Databases = ["Sales"], BackupDirectory = directory };
await jobs.SaveAsync(job, default);
await jobs.SaveAsync(job, default);
Check((await store.GetJobsAsync()).Count == 1, "retry with stable job ID does not duplicate job");
Check((await store.GetJobsAsync()).Single().SqlServer is null, "new job contains no duplicate SQL configuration");
try { await service.DeleteAsync(saved.Id, default); throw new Exception("Expected conflict"); }
catch (ConfigurationException e) { Check(e.Code == "CONNECTION_IN_USE", "cannot delete referenced connection"); }
provider.Status = "OFFLINE";
try { await jobs.SaveAsync(job, default); throw new Exception("Expected offline rejection"); }
catch (ConfigurationException e) { Check(e.Code == "DATABASE_UNAVAILABLE", "save rechecks offline state"); }
provider.Status = "ONLINE"; provider.Accessible = false;
try { await jobs.SaveAsync(job, default); throw new Exception("Expected access rejection"); }
catch (ConfigurationException e) { Check(e.Code == "DATABASE_UNAVAILABLE", "save rejects inaccessible databases"); }
provider.Accessible = true;
var connection = await service.GetAsync(saved.Id);
var legacyOptions = SqlServerConnectionSettings.ForBackup(connection);
Check(legacyOptions.Server == @"db.example\REPORTS" && await vault.ResolveAsync(legacyOptions.PasswordSecret) == "replacement", "backup bridge resolves saved configuration and vault reference");
var legacyJob = new BackupJob { Name = "Legacy", SqlServer = new() { Server = "legacy-host" }, Databases = ["LegacyDb"], BackupDirectory = directory };
await store.SaveJobAsync(legacyJob);
await jobs.SaveAsync(legacyJob, default);
Check((await store.GetJobsAsync()).Any(j => j.SqlServer?.Server == "legacy-host" && j.ConnectionId is null), "legacy job remains supported");
await Task.WhenAll(Enumerable.Range(0, 12).Select(i => vault.StoreAsync($"concurrent-{i}", $"value-{i}")));
Check((await Task.WhenAll(Enumerable.Range(0, 12).Select(i => vault.ResolveAsync($"concurrent-{i}")))).Count(v => v is not null) == 12, "concurrent vault writes preserve all secrets");
await Task.WhenAll(Enumerable.Range(0, 12).Select(i => store.SaveJobAsync(new BackupJob { Name = $"parallel-{i}" })));
Check((await store.GetJobsAsync()).Count == 14, "concurrent JSON mutations preserve jobs");
await service.DeleteAsync((await service.SaveAsync(null, options, default)).Id, default);
Check((await store.GetConnectionsAsync()).Count == 1, "unused connection can be deleted");
try { await service.SaveAsync(saved.Id, sqlOptions with { Host = "different-server", Password = "replacement" }, default); throw new Exception("Referenced server changed"); }
catch (ConfigurationException e) { Check(e.Code == "CONNECTION_IN_USE", "referenced connection cannot switch backup target"); }
provider.DuringDiscovery = async () => await store.SaveConnectionAsync(connection with { UpdatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(1) });
try { await jobs.SaveAsync(job, default); throw new Exception("Stale discovery accepted"); }
catch (ConfigurationException e) { Check(e.Code == "CONNECTION_CHANGED", "concurrent connection change invalidates job validation"); }
provider.DuringDiscovery = null;
var arguments = SqlServerConnectionSettings.BuildSqlcmdArguments(legacyOptions, "-N[s|m|o] (encrypt connection)");
Check(arguments.Contains("-x"), "sqlcmd variable substitution disabled");
Check(arguments.Contains("-Nm"), "explicit mandatory encryption");
Check(SqlServerConnectionSettings.BuildSqlcmdArguments(legacyOptions with { Encrypt = false }, "-N[s|m|o]").Contains("-No"), "explicit optional encryption");
try { SqlServerConnectionSettings.BuildSqlcmdArguments(legacyOptions, "old sqlcmd -N"); throw new Exception("Old encryption flags accepted"); }
catch (ConfigurationException e) { Check(e.Code == "SQLCMD_UNSUPPORTED", "unsupported sqlcmd fails with clear deployment message"); }
Check(SqlServerConnectionSettings.BuildSqlcmdArguments(new SqlServerOptions { Server = "legacy" }, null).Contains("-x"), "legacy backup stays supported with substitution disabled");
Console.WriteLine($"{passed} contract assertions passed. Test artifacts: {directory}");

sealed class TestProvider : IDatabaseProvider
{
    public string Provider => "sqlserver";
    public bool Fail { get; set; }
    public string Status { get; set; } = "ONLINE";
    public bool Accessible { get; set; } = true;
    public Func<Task>? DuringDiscovery { get; set; }
    public Task<DatabaseConnectionTestResult> TestConnectionAsync(DatabaseConnectionOptions o, CancellationToken ct) => Task.FromResult(new DatabaseConnectionTestResult(true, o.Host, "Microsoft SQL Server", "test", "ok"));
    public async Task<DatabaseDiscoveryResult> DiscoverDatabasesAsync(DatabaseConnectionOptions o, CancellationToken ct)
    {
        if (Fail) throw new ConfigurationException("CONNECTION_FAILED", "Test failure");
        if (DuringDiscovery is not null) await DuringDiscovery();
        return new DatabaseDiscoveryResult(true, new(o.Host, Provider, "test"), [new("Sales", Status, 100, null, Accessible)]);
    }
}
sealed class TestEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Testing";
    public string ApplicationName { get; set; } = "BackupManager.Tests";
    public string ContentRootPath { get; set; } = "";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
