using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace Miqat.API.Hubs
{
    /// <summary>
    /// The app's single realtime channel. Each connection is joined to a
    /// per-user group on connect, so the server can address "every tab this
    /// person has open" without tracking connection ids itself — SignalR
    /// removes the connection from the group automatically on disconnect.
    /// </summary>
    [Authorize]
    public class MiqatHub : Hub
    {
        public static string UserGroup(string userId) => $"user:{userId}";

        public override async Task OnConnectedAsync()
        {
            var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(userId))
                await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));

            await base.OnConnectedAsync();
        }
    }
}
