using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackupManager.API.Endpoints;
using BackupManager.Application.Connections;
using BackupManager.Domain.Models;
using BackupManager.Infrastructure.Persistence;
using BackupManager.Infrastructure.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

// Real HTTP + SQLite, with a deterministic database provider only in this test assembly.
internal static class HttpPersistenceTests
{
    public static async Task RunAsync(Action<bool, string> check, string root)
    {
        var path = Path.Combine(root, "http"); Directory.CreateDirectory(Path.Combine(path, "data"));
        var provisionedKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        async Task<WebApplication> Start()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = path });
            builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["BackupManager:Security:MasterKey"] = provisionedKey });
            builder.Services.AddDbContextFactory<MetadataDbContext>(o => o.UseSqlite($"Data Source={Path.Combine(path, "data", "backupmanager.db")};Foreign Keys=True"));
            builder.Services.AddSingleton<JsonStore>(); builder.Services.AddSingleton<ISecretVault, SecretVault>();
            builder.Services.AddSingleton<SqliteConfigurationStore>();
            builder.Services.AddSingleton<IConfigurationStore>(s => s.GetRequiredService<SqliteConfigurationStore>());
            builder.Services.AddSingleton<IDatabaseProvider, TestProvider>(); builder.Services.AddSingleton<IDatabaseProviderResolver, DatabaseProviderResolver>();
            builder.Services.AddSingleton<DatabaseConnectionService>(); builder.Services.AddSingleton<BackupJobService>();
            var app = builder.Build();
            await app.Services.GetRequiredService<SqliteConfigurationStore>().InitializeAsync(app.Services.GetRequiredService<JsonStore>(), app.Services.GetRequiredService<ISecretVault>());
            var group = app.MapGroup("/api/v1").AddEndpointFilter<ConfigurationErrorFilter>();
            group.MapDatabaseConnections();
            group.MapPost("/jobs", (BackupJob job, BackupJobService service, CancellationToken ct) => service.SaveAsync(job, ct));
            await app.StartAsync(); return app;
        }
        var input = new DatabaseConnectionOptions { Name = "HTTP persistence test", Host = "db.example", AuthenticationType = "sqlserver", Username = "test", Password = "test-only-secret" };
        Guid id;
        await using (var app = await Start())
        {
            using var client = new HttpClient { BaseAddress = new(app.Urls.Single()) };
            var response = await client.PostAsJsonAsync("/api/v1/database-connections", input);
            check(response.StatusCode == HttpStatusCode.Created, "HTTP POST creates independent saved resource");
            var body = await response.Content.ReadAsStringAsync(); using var doc = JsonDocument.Parse(body);
            id = doc.RootElement.GetProperty("id").GetGuid();
            check(doc.RootElement.GetProperty("name").GetString() == input.Name && !doc.RootElement.TryGetProperty("configuration", out _), "HTTP returns flat metadata");
            check(!body.Contains("test-only-secret") && !doc.RootElement.TryGetProperty("password", out _) && !doc.RootElement.TryGetProperty("encryptedPassword", out _), "HTTP POST never exposes credentials");
            await using var db = await app.Services.GetRequiredService<IDbContextFactory<MetadataDbContext>>().CreateDbContextAsync();
            check(await db.DatabaseConnections.CountAsync() == 1, "POST commits an actual SQLite row");
            await app.StopAsync();
        }
        await using (var app = await Start())
        {
            using var client = new HttpClient { BaseAddress = new(app.Urls.Single()) };
            using var list = JsonDocument.Parse(await client.GetStringAsync("/api/v1/database-connections"));
            check(list.RootElement.GetArrayLength() == 1 && list.RootElement[0].GetProperty("id").GetGuid() == id, "HTTP GET retains same ID after complete host restart");
            var get = await client.GetStringAsync($"/api/v1/database-connections/{id}");
            using var doc = JsonDocument.Parse(get);
            check(!doc.RootElement.TryGetProperty("password", out _) && !doc.RootElement.TryGetProperty("encryptedPassword", out _), "restarted GET metadata has no password fields");
            foreach (var operation in new[] { "test", "discover" })
                check((await client.PostAsync($"/api/v1/database-connections/{id}/{operation}", null)).IsSuccessStatusCode, $"saved-ID {operation} needs no browser password");
            var before = (await app.Services.GetRequiredService<DatabaseConnectionService>().GetAsync(id)).EncryptedPassword;
            check((await client.PutAsJsonAsync($"/api/v1/database-connections/{id}", input with { Password = "", Port = 1500 })).IsSuccessStatusCode, "PUT edits same resource with blank password");
            var after = await app.Services.GetRequiredService<DatabaseConnectionService>().GetAsync(id);
            check(after.EncryptedPassword == before && after.Settings.Port == 1500, "blank edit preserves identical ciphertext and custom port");
            var job = new BackupJob { Name = "HTTP job", ConnectionId = id, Databases = ["Sales"], BackupDirectory = path };
            check((await client.PostAsJsonAsync("/api/v1/jobs", job)).IsSuccessStatusCode, "HTTP creates job using ConnectionId");
            check((await client.DeleteAsync($"/api/v1/database-connections/{id}")).StatusCode == HttpStatusCode.Conflict, "HTTP DELETE referenced connection returns 409");
            await using var db = await app.Services.GetRequiredService<IDbContextFactory<MetadataDbContext>>().CreateDbContextAsync();
            check(await db.BackupJobDatabases.CountAsync() == 1 && (await db.BackupJobs.SingleAsync()).LegacySqlServerJson is null, "job database relationship persists without copied connection credentials");
            try { await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM DatabaseConnections WHERE Id={id}"); check(false, "SQLite foreign key restrict"); }
            catch (Microsoft.Data.Sqlite.SqliteException e) { check(e.SqliteErrorCode == 19, "SQLite FK blocks direct deletion outside API"); }
            await app.StopAsync();
        }
    }
}
