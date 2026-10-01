using BackupManager.API.Endpoints;
using BackupManager.API.Hubs;
using BackupManager.API.Services;
using BackupManager.Application.Connections;
using BackupManager.Domain.Models;
using BackupManager.Infrastructure.Persistence;
using BackupManager.Infrastructure.Services;
using BackupManager.Plugins.SqlServer;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

var builder =
    WebApplication.CreateBuilder(args);

// Validate before migration, legacy import,
// or opening the HTTP listener.
var secretVault =
    new SecretVault(
        builder.Environment,
        builder.Configuration
    );

builder.Host.UseWindowsService();

builder.Services.AddSingleton<JsonStore>();
builder.Services.AddSingleton(secretVault);
builder.Services.AddSingleton<ProcessRunner>();
builder.Services.AddSingleton<SqlServerPlugin>();

builder.Services.Configure<RemoteBackupStorageOptions>(
    builder.Configuration.GetSection(
        RemoteBackupStorageOptions.SectionName
    )
);

builder.Services.AddSingleton<
    RemoteBackupStorageService
>();

var metadataDirectory =
    Path.Combine(
        builder.Environment.ContentRootPath,
        "data"
    );

Directory.CreateDirectory(
    metadataDirectory
);

builder.Services.AddDbContextFactory<
    MetadataDbContext
>(
    options =>
        options.UseSqlite(
            new Microsoft.Data.Sqlite
                .SqliteConnectionStringBuilder
            {
                DataSource =
                    Path.Combine(
                        metadataDirectory,
                        "backupmanager.db"
                    ),

                ForeignKeys = true,
            }.ToString()
        )
);

builder.Services.AddSingleton<
    SqliteConfigurationStore
>();

builder.Services.AddSingleton<
    IConfigurationStore
>(
    sp =>
        sp.GetRequiredService<
            SqliteConfigurationStore
        >()
);

builder.Services.AddSingleton<
    ISecretVault
>(
    sp =>
        sp.GetRequiredService<
            SecretVault
        >()
);

builder.Services.AddSingleton<
    IDatabaseProvider,
    SqlServerDatabaseProvider
>();

builder.Services.AddSingleton<
    IDatabaseProviderResolver,
    DatabaseProviderResolver
>();

builder.Services.AddSingleton<
    DatabaseConnectionService
>();

builder.Services.AddSingleton<
    BackupJobService
>();

builder.Services.AddSingleton<
    BackupPipeline
>();

//
// One shared RunCoordinator instance.
//
// It is both:
// - injectable as RunCoordinator
// - hosted as the single FIFO backup worker
//
builder.Services.AddSingleton<
    RunCoordinator
>();

builder.Services.AddHostedService(
    sp =>
        sp.GetRequiredService<
            RunCoordinator
        >()
);

//
// Automatic backup scheduler.
// Runs inside the BackupManager host.
//
builder.Services.AddHostedService<
    BackupSchedulerService
>();

builder.Services.AddHttpClient();
builder.Services.AddSignalR();

builder.Services.ConfigureHttpJsonOptions(
    options =>
        options.SerializerOptions
            .Converters
            .Add(
                new JsonStringEnumConverter()
            )
);

builder.Services.AddCors(
    options =>
        options.AddDefaultPolicy(
            policy =>
                policy
                    .WithOrigins(
                        builder.Configuration[
                            "FrontendOrigin"
                        ]
                        ?? "http://localhost:3000"
                    )
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials()
        )
);

var app =
    builder.Build();

//
// Initialize SQLite metadata store and import
// legacy configuration before the HTTP listener
// begins accepting requests.
//
await app
    .Services
    .GetRequiredService<
        SqliteConfigurationStore
    >()
    .InitializeAsync(
        app.Services
            .GetRequiredService<
                JsonStore
            >(),

        app.Services
            .GetRequiredService<
                ISecretVault
            >()
    );

//
// Recover runs left active by the previous
// BackupManager process.
//
// RunCoordinator's queue is in-memory.
// Any BackupRun still marked Running before this
// process starts cannot belong to the new worker.
//
var runStore =
    app.Services.GetRequiredService<
        JsonStore
    >();

var recoveredRuns =
    await runStore
        .RecoverInterruptedRunsAsync();

if (recoveredRuns > 0)
{
    app.Logger.LogWarning(
        "Recovered {RecoveredRunCount} interrupted backup run(s) from the previous BackupManager process.",
        recoveredRuns
    );
}

app.Use(
    async (context, next) =>
    {
        try
        {
            await next(context);
        }
        catch (BadHttpRequestException)
        {
            context.Response.StatusCode =
                400;

            await context.Response
                .WriteAsJsonAsync(
                    new
                    {
                        success = false,

                        message =
                            "JSON request không hợp lệ.",

                        errorCode =
                            "VALIDATION_FAILED",
                    }
                );
        }
    }
);

app.UseCors();

var api =
    app.MapGroup(
            "/api/v1"
        )
        .AddEndpointFilter<
            ConfigurationErrorFilter
        >();

api.MapDatabaseConnections();

api.MapGet(
    "/health",
    () =>
        Results.Ok(
            new
            {
                status =
                    "healthy",

                version =
                    "0.4.0"
            }
        )
);

api.MapGet(
    "/jobs",
    async (
        IConfigurationStore store
    ) =>
        Results.Ok(
            await store.GetJobsAsync()
        )
);

api.MapPost(
    "/jobs",
    async (
        BackupJob job,
        BackupJobService service,
        CancellationToken ct
    ) =>
        Results.Ok(
            await service.SaveAsync(
                job,
                ct
            )
        )
);

api.MapPost(
    "/secrets",
    async (
        SecretInput input,
        SecretVault vault
    ) =>
    {
        await vault.StoreAsync(
            input.Name,
            input.Value
        );

        return Results.NoContent();
    }
);

api.MapPost(
    "/connections/sql-server/databases",
    async (
        SqlServerOptions options,
        SqlServerPlugin plugin,
        CancellationToken ct
    ) =>
        Results.Ok(
            await plugin.DiscoverAsync(
                options,
                ct
            )
        )
);

api.MapGet(
    "/runs",
    async (
        JsonStore store
    ) =>
        Results.Ok(
            await store.GetRunsAsync()
        )
);

api.MapGet(
    "/runs/{id:guid}",
    async (
        Guid id,
        JsonStore store
    ) =>
        (
            await store.GetRunsAsync()
        ).FirstOrDefault(
            x => x.Id == id
        ) is { } run
            ? Results.Ok(run)
            : Results.NotFound()
);

//
// Runtime queue state.
//
// This endpoint intentionally reports the live,
// in-memory RunCoordinator state rather than
// inferring queue state from persisted BackupRun
// history.
//
api.MapGet(
    "/runtime",
    (
        RunCoordinator coordinator
    ) =>
        Results.Ok(
            coordinator.GetSnapshot()
        )
);

api.MapPost(
    "/jobs/{id:guid}/run",
    async (
        Guid id,
        IConfigurationStore store,
        RunCoordinator coordinator
    ) =>
    {
        var job =
            (
                await store.GetJobsAsync()
            ).FirstOrDefault(
                x => x.Id == id
            );

        if (job is null)
        {
            return Results.NotFound();
        }

        return coordinator.TryStart(job)
            ? Results.Accepted()
            : Results.Conflict(
                new
                {
                    error =
                        "Job is already queued or running."
                }
            );
    }
);

api.MapPost(
    "/jobs/{id:guid}/cancel",
    (
        Guid id,
        RunCoordinator coordinator
    ) =>
        coordinator.Cancel(id)
            ? Results.Accepted()
            : Results.NotFound()
);

api.MapPost(
    "/storage/test",
    async (
        RemoteBackupStorageService storage,
        CancellationToken ct
    ) =>
    {
        if (!storage.Enabled)
        {
            return Results.BadRequest(
                new
                {
                    success = false,

                    message =
                        "Remote storage is disabled."
                }
            );
        }

        await storage.TestAsync(ct);

        return Results.Ok(
            new
            {
                success = true,

                message =
                    "SSH + rclone + Google Drive connection successful."
            }
        );
    }
);

app.MapHub<RunHub>(
    "/hubs/runs"
);

app.Run();