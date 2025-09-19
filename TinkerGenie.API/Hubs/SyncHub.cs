using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json;
using TinkerGenie.API.Services;

namespace TinkerGenie.API.Hubs
{
    [Authorize]
    public class SyncHub : Hub
    {
        private readonly ILogger<SyncHub> _logger;
        private readonly IConversationService? _conversationService;

        public SyncHub(ILogger<SyncHub> logger, IConversationService? conversationService = null)
        {
            _logger = logger;
            _conversationService = conversationService;
        }

        public override async Task OnConnectedAsync()
        {
            var userId = Context.User?.FindFirst("userId")?.Value;
            
            if (!string.IsNullOrEmpty(userId))
            {
                // Add user to their own group for cross-device sync
                await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{userId}");
                _logger.LogInformation("SyncHub: Added connection {ConnectionId} to group user-{UserId}", 
                    Context.ConnectionId, userId);
            }
            
            _logger.LogInformation("SyncHub client connected: {ConnectionId} for user {UserId}", 
                Context.ConnectionId, userId ?? "anonymous");
            
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Context.User?.FindFirst("userId")?.Value;
            
            if (!string.IsNullOrEmpty(userId))
            {
                // Remove from user group
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user-{userId}");
                _logger.LogInformation("SyncHub: Removed connection {ConnectionId} from group user-{UserId}", 
                    Context.ConnectionId, userId);
            }
            
            if (exception != null)
            {
                _logger.LogError(exception, "SyncHub client disconnected with error: {ConnectionId}", 
                    Context.ConnectionId);
            }
            else
            {
                _logger.LogInformation("SyncHub client disconnected: {ConnectionId}", Context.ConnectionId);
            }
            
            await base.OnDisconnectedAsync(exception);
        }

        // Sync a new message to all user's devices
        public async Task SyncNewMessage(string message, string conversationId, bool isUserMessage)
        {
            try
            {
                var userId = Context.User?.FindFirst("userId")?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    await Clients.Caller.SendAsync("SyncError", "User not authenticated");
                    return;
                }
                
                _logger.LogInformation("SyncHub: Broadcasting message to all devices for user {UserId}", userId);
                
                var syncData = new
                {
                    type = "newMessage",
                    message = message,
                    conversationId = conversationId,
                    isUserMessage = isUserMessage,
                    timestamp = DateTime.UtcNow,
                    fromDevice = Context.ConnectionId
                };
                
                // Broadcast to all user's connected devices EXCEPT the sender
                await Clients.OthersInGroup($"user-{userId}").SendAsync("MessageSynced", syncData);
                
                // Confirm to sender
                await Clients.Caller.SendAsync("SyncConfirmed", new { success = true, type = "message" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing message for user");
                await Clients.Caller.SendAsync("SyncError", "Failed to sync message");
            }
        }

        // Sync conversation deletion to all devices
        public async Task SyncDeleteConversation(string conversationId)
        {
            try
            {
                var userId = Context.User?.FindFirst("userId")?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    await Clients.Caller.SendAsync("SyncError", "User not authenticated");
                    return;
                }
                
                _logger.LogInformation("SyncHub: Broadcasting conversation deletion for user {UserId}", userId);
                
                var syncData = new
                {
                    type = "deleteConversation",
                    conversationId = conversationId,
                    timestamp = DateTime.UtcNow,
                    fromDevice = Context.ConnectionId
                };
                
                // Broadcast to all user's connected devices EXCEPT the sender
                await Clients.OthersInGroup($"user-{userId}").SendAsync("ConversationDeleted", syncData);
                
                // Confirm to sender
                await Clients.Caller.SendAsync("SyncConfirmed", new { success = true, type = "delete" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing conversation deletion");
                await Clients.Caller.SendAsync("SyncError", "Failed to sync deletion");
            }
        }

        // Sync settings update to all devices
        public async Task SyncSettingsUpdate(string settings)
        {
            try
            {
                var userId = Context.User?.FindFirst("userId")?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    await Clients.Caller.SendAsync("SyncError", "User not authenticated");
                    return;
                }
                
                _logger.LogInformation("SyncHub: Broadcasting settings update for user {UserId}", userId);
                
                var syncData = new
                {
                    type = "settingsUpdate",
                    settings = settings,
                    timestamp = DateTime.UtcNow,
                    fromDevice = Context.ConnectionId
                };
                
                // Broadcast to all user's connected devices EXCEPT the sender
                await Clients.OthersInGroup($"user-{userId}").SendAsync("SettingsUpdated", syncData);
                
                // Confirm to sender
                await Clients.Caller.SendAsync("SyncConfirmed", new { success = true, type = "settings" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing settings");
                await Clients.Caller.SendAsync("SyncError", "Failed to sync settings");
            }
        }

        // Request full sync from server (useful when reconnecting)
        public async Task RequestFullSync()
        {
            try
            {
                var userId = Context.User?.FindFirst("userId")?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    await Clients.Caller.SendAsync("SyncError", "User not authenticated");
                    return;
                }
                
                _logger.LogInformation("SyncHub: Full sync requested for user {UserId}", userId);
                
                // Get recent conversations from database if service is available
                if (_conversationService != null)
                {
                    // TODO: Implement GetRecentConversations
                    // var recentMessages = await _conversationService.GetRecentConversations(userId, 50);
                    var recentMessages = new List<object>(); // Placeholder
                    
                    await Clients.Caller.SendAsync("FullSyncData", new
                    {
                        type = "fullSync",
                        messages = recentMessages,
                        timestamp = DateTime.UtcNow
                    });
                }
                else
                {
                    await Clients.Caller.SendAsync("FullSyncData", new
                    {
                        type = "fullSync",
                        messages = new List<object>(),
                        timestamp = DateTime.UtcNow,
                        note = "Conversation service not available"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing full sync request");
                await Clients.Caller.SendAsync("SyncError", "Failed to retrieve sync data");
            }
        }

        // Heartbeat/keepalive
        public async Task Ping()
        {
            await Clients.Caller.SendAsync("Pong", new { timestamp = DateTime.UtcNow });
        }

        // Get connected devices count for current user
        public async Task GetConnectedDevices()
        {
            try
            {
                var userId = Context.User?.FindFirst("userId")?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    await Clients.Caller.SendAsync("ConnectedDevices", new { count = 0 });
                    return;
                }
                
                // Note: SignalR doesn't provide a built-in way to count group members
                // In production, you'd track this in Redis or a similar store
                await Clients.Caller.SendAsync("ConnectedDevices", new 
                { 
                    count = "unknown",
                    note = "Device tracking would require Redis implementation"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting connected devices");
                await Clients.Caller.SendAsync("ConnectedDevices", new { count = 0, error = true });
            }
        }
    }
}