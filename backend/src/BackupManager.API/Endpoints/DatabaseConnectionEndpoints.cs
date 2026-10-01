using BackupManager.API.Services;
using BackupManager.Application.Connections;
using BackupManager.Domain.Models;

namespace BackupManager.API.Endpoints;

public static class DatabaseConnectionEndpoints
{
    public static void MapDatabaseConnections(
        this RouteGroupBuilder api
    )
    {
        var group =
            api.MapGroup(
                "/database-connections"
            );

        group.MapPost(
            "/test",
            (
                DatabaseConnectionOptions input,
                DatabaseConnectionService service,
                CancellationToken ct
            ) =>
                service.TestAsync(
                    input,
                    ct
                )
        );

        group.MapPost(
            "/discover",
            (
                DatabaseConnectionOptions input,
                DatabaseConnectionService service,
                CancellationToken ct
            ) =>
                service.DiscoverAsync(
                    input,
                    ct
                )
        );

        group.MapPost(
            "/{id:guid}/test",
            async (
                Guid id,
                DatabaseConnectionService service,
                CancellationToken ct
            ) =>
                await service.TestAsync(
                    await service.ResolveOptionsAsync(id),
                    ct
                )
        );

        group.MapPost(
            "/{id:guid}/discover",
            async (
                Guid id,
                DatabaseConnectionService service,
                CancellationToken ct
            ) =>
                await service.DiscoverAsync(
                    await service.ResolveOptionsAsync(id),
                    ct
                )
        );

        group.MapPost(
            "/{id:guid}/test-options",
            async (
                Guid id,
                DatabaseConnectionOptions input,
                DatabaseConnectionService service,
                CancellationToken ct
            ) =>
                await service.TestAsync(
                    await service.ResolveEditOptionsAsync(
                        id,
                        input
                    ),
                    ct
                )
        );

        group.MapGet(
            "/",
            (
                DatabaseConnectionService service
            ) =>
                service.ListAsync()
        );

        group.MapGet(
            "/{id:guid}",
            async (
                Guid id,
                DatabaseConnectionService service
            ) =>
                DatabaseConnectionView.From(
                    await service.GetAsync(id)
                )
        );

        group.MapPost(
            "/",
            async (
                DatabaseConnectionOptions input,
                DatabaseConnectionService service,
                CancellationToken ct
            ) =>
            {
                var connection =
                    await service.SaveAsync(
                        null,
                        input,
                        ct
                    );

                return Results.Created(
                    $"/api/v1/database-connections/{connection.Id}",
                    connection
                );
            }
        );

        group.MapPut(
            "/{id:guid}",
            (
                Guid id,
                DatabaseConnectionOptions input,
                DatabaseConnectionService service,
                CancellationToken ct
            ) =>
                service.SaveAsync(
                    id,
                    input,
                    ct
                )
        );

        //
        // Save only SSH/rclone transport.
        //
        // This does not modify SQL settings
        // or SQL credentials.
        //
        group.MapPut(
            "/{id:guid}/backup-transport",
            (
                Guid id,
                BackupTransportSettings input,
                DatabaseConnectionService service,
                CancellationToken ct
            ) =>
                service.SaveBackupTransportAsync(
                    id,
                    input,
                    ct
                )
        );

        //
        // Test the SAVED transport for this
        // database connection.
        //
        // No database backup is executed.
        //
        group.MapPost(
            "/{id:guid}/backup-transport/test",
            async (
                Guid id,
                DatabaseConnectionService service,
                RemoteBackupStorageService storage,
                CancellationToken ct
            ) =>
            {
                var connection =
                    await service.GetAsync(id);

                var transport =
                    connection.Settings.BackupTransport;

                if (transport is null)
                {
                    return Results.BadRequest(
                        new
                        {
                            success = false,

                            message =
                                "Connection chưa có Backup Transport riêng."
                        }
                    );
                }

                if (!transport.Enabled)
                {
                    return Results.BadRequest(
                        new
                        {
                            success = false,

                            message =
                                "Backup Transport của connection đang bị tắt."
                        }
                    );
                }

                await storage.TestAsync(
                    transport,
                    ct
                );

                return Results.Ok(
                    new
                    {
                        success = true,

                        message =
                            $"SSH + rclone + Google Drive connection successful. Host: {transport.SshHost}, Remote: {transport.RcloneRemote}."
                    }
                );
            }
        );

        group.MapDelete(
            "/{id:guid}",
            async (
                Guid id,
                DatabaseConnectionService service,
                CancellationToken ct
            ) =>
            {
                await service.DeleteAsync(
                    id,
                    ct
                );

                return Results.NoContent();
            }
        );
    }
}