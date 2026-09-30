using BackupManager.Domain.Models;
using BackupManager.Infrastructure.Services;
using BackupManager.API.Services;
using BackupManager.Plugins.SqlServer;
using BackupManager.API.Hubs;
using System.Text.Json.Serialization;
using BackupManager.Application.Connections;
using BackupManager.API.Endpoints;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService();
builder.Services.AddSingleton<JsonStore>();
builder.Services.AddSingleton<SecretVault>();
builder.Services.AddSingleton<ProcessRunner>();
builder.Services.AddSingleton<SqlServerPlugin>();
builder.Services.AddSingleton<IConfigurationStore>(sp => sp.GetRequiredService<JsonStore>());
builder.Services.AddSingleton<ISecretVault>(sp => sp.GetRequiredService<SecretVault>());
builder.Services.AddSingleton<IDatabaseProvider, SqlServerDatabaseProvider>();
builder.Services.AddSingleton<IDatabaseProviderResolver, DatabaseProviderResolver>();
builder.Services.AddSingleton<DatabaseConnectionService>();
builder.Services.AddSingleton<BackupJobService>();
builder.Services.AddSingleton<BackupPipeline>();
builder.Services.AddSingleton<RunCoordinator>();
builder.Services.AddHttpClient();
builder.Services.AddSignalR();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration["FrontendOrigin"] ?? "http://localhost:3000")
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();
app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (BadHttpRequestException)
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsJsonAsync(new { success = false, message = "JSON request không hợp lệ.", errorCode = "VALIDATION_FAILED" });
    }
});
app.UseCors();
var api = app.MapGroup("/api/v1").AddEndpointFilter<ConfigurationErrorFilter>();
api.MapDatabaseConnections();
api.MapGet("/health", () => Results.Ok(new { status = "healthy", version = "0.2.0" }));
api.MapGet("/jobs", async (JsonStore store) => Results.Ok(await store.GetJobsAsync()));
api.MapPost("/jobs", async (BackupJob job, BackupJobService service, CancellationToken ct) => Results.Ok(await service.SaveAsync(job, ct)));
api.MapPost("/secrets", async (SecretInput input, SecretVault vault) => { await vault.StoreAsync(input.Name, input.Value); return Results.NoContent(); });
api.MapPost("/connections/sql-server/databases", async (SqlServerOptions options, SqlServerPlugin plugin, CancellationToken ct) => Results.Ok(await plugin.DiscoverAsync(options, ct)));
api.MapGet("/runs", async (JsonStore store) => Results.Ok(await store.GetRunsAsync()));
api.MapGet("/runs/{id:guid}", async (Guid id, JsonStore store) => (await store.GetRunsAsync()).FirstOrDefault(x => x.Id == id) is { } run ? Results.Ok(run) : Results.NotFound());
api.MapPost("/jobs/{id:guid}/run", async (Guid id, JsonStore store, RunCoordinator coordinator) =>
{
    var job = (await store.GetJobsAsync()).FirstOrDefault(x => x.Id == id);
    if (job is null) return Results.NotFound();
    return coordinator.TryStart(job) ? Results.Accepted() : Results.Conflict(new { error = "Job is already running." });
});
api.MapPost("/jobs/{id:guid}/cancel", (Guid id, RunCoordinator coordinator) => coordinator.Cancel(id) ? Results.Accepted() : Results.NotFound());
app.MapHub<RunHub>("/hubs/runs");
app.Run();
