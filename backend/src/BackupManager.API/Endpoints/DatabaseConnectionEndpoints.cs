using BackupManager.Application.Connections;

namespace BackupManager.API.Endpoints;

public static class DatabaseConnectionEndpoints
{
    public static void MapDatabaseConnections(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/database-connections");
        group.MapPost("/test", (DatabaseConnectionOptions input, DatabaseConnectionService service, CancellationToken ct) => service.TestAsync(input, ct));
        group.MapPost("/discover", (DatabaseConnectionOptions input, DatabaseConnectionService service, CancellationToken ct) => service.DiscoverAsync(input, ct));
        group.MapGet("/", (DatabaseConnectionService service) => service.ListAsync());
        group.MapGet("/{id:guid}", async (Guid id, DatabaseConnectionService service) => DatabaseConnectionView.From(await service.GetAsync(id)));
        group.MapPost("/", async (DatabaseConnectionOptions input, DatabaseConnectionService service, CancellationToken ct) =>
        {
            var connection = await service.SaveAsync(null, input, ct);
            return Results.Created($"/api/v1/database-connections/{connection.Id}", connection);
        });
        group.MapPut("/{id:guid}", (Guid id, DatabaseConnectionOptions input, DatabaseConnectionService service, CancellationToken ct) => service.SaveAsync(id, input, ct));
        group.MapDelete("/{id:guid}", async (Guid id, DatabaseConnectionService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return Results.NoContent();
        });
    }
}
