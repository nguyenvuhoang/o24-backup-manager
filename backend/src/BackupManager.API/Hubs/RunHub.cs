using Microsoft.AspNetCore.SignalR;

namespace BackupManager.API.Hubs;

public sealed class RunHub : Hub
{
    public Task Subscribe(Guid runId) => Groups.AddToGroupAsync(Context.ConnectionId, runId.ToString("N"));
    public Task Unsubscribe(Guid runId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, runId.ToString("N"));
}
