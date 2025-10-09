using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using System.Security.Claims;

namespace TinkerGenie.API.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly ILogger<ChatHub> _logger;

        public ChatHub(ILogger<ChatHub> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        private string GetUserId()
        {
            try
            {
                var userId = Context.User?.FindFirst("user_id")?.Value 
                    ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? Context.User?.FindFirst("sub")?.Value;
                
                if (string.IsNullOrEmpty(userId))
                {
                    _logger.LogWarning($"No user ID found in SignalR connection: {Context.ConnectionId}");
                    return "anonymous";
                }
                
                return userId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error extracting user ID from SignalR context: {Context.ConnectionId}");
                return "anonymous";
            }
        }

        public override async Task OnConnectedAsync()
        {
            try
            {
                var userId = GetUserId();
                var userName = Context.User?.Identity?.Name ?? "Anonymous";
                
                _logger.LogInformation($"🔗 SignalR Connected: {Context.ConnectionId}, User: {userName} ({userId})");
                
                // Join user to their personal group
                if (userId != "anonymous")
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
                    _logger.LogInformation($"👥 User {userId} joined personal group");
                }
                
                // Join general chat group
                await Groups.AddToGroupAsync(Context.ConnectionId, "general_chat");
                
                // Notify user of successful connection
                await Clients.Caller.SendAsync("ConnectionEstablished", new
                {
                    connectionId = Context.ConnectionId,
                    userId = userId,
                    userName = userName,
                    timestamp = DateTime.UtcNow,
                    message = "Welcome to TinkerGenie! Your real-time connection is active."
                });
                
                await base.OnConnectedAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in OnConnectedAsync: {Context.ConnectionId}");
                await Clients.Caller.SendAsync("Error", "Connection error occurred");
            }
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            try
            {
                var userId = GetUserId();
                var userName = Context.User?.Identity?.Name ?? "Anonymous";
                
                if (exception != null)
                {
                    _logger.LogWarning(exception, $"🔌 SignalR Disconnected with error: {Context.ConnectionId}, User: {userName}");
            }
            else
            {
                    _logger.LogInformation($"🔌 SignalR Disconnected: {Context.ConnectionId}, User: {userName}");
                }
                
                await base.OnDisconnectedAsync(exception);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in OnDisconnectedAsync: {Context.ConnectionId}");
            }
        }

        public async Task JoinUserGroup(string userId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(userId))
                {
                    await Clients.Caller.SendAsync("Error", "Invalid user ID");
                    return;
                }
                
                await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
                _logger.LogInformation($"👥 Connection {Context.ConnectionId} joined user group: user_{userId}");
                
                await Clients.Caller.SendAsync("GroupJoined", $"user_{userId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error joining user group: {userId}");
                await Clients.Caller.SendAsync("Error", "Failed to join user group");
            }
        }

        public async Task JoinGroup(string groupName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(groupName))
                {
                    await Clients.Caller.SendAsync("Error", "Invalid group name");
                    return;
                }
                
                await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
                _logger.LogInformation($"👥 Connection {Context.ConnectionId} joined group: {groupName}");
                
                await Clients.Caller.SendAsync("GroupJoined", groupName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error joining group: {groupName}");
                await Clients.Caller.SendAsync("Error", "Failed to join group");
            }
        }

        public async Task LeaveGroup(string groupName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(groupName))
                {
                    await Clients.Caller.SendAsync("Error", "Invalid group name");
                    return;
                }
                
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
                _logger.LogInformation($"👋 Connection {Context.ConnectionId} left group: {groupName}");
                
                await Clients.Caller.SendAsync("GroupLeft", groupName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error leaving group: {groupName}");
                await Clients.Caller.SendAsync("Error", "Failed to leave group");
            }
        }

        public async Task SendMessage(string message, string? conversationId = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(message))
                {
                    await Clients.Caller.SendAsync("Error", "Message cannot be empty");
                    return;
                }
                
                var userId = GetUserId();
                var userName = Context.User?.Identity?.Name ?? "Anonymous";
                
                _logger.LogInformation($"💬 Message from {userName} ({userId}): {message.Substring(0, Math.Min(50, message.Length))}...");
                
                // Create message object
                var messageObj = new
                {
                    id = Guid.NewGuid().ToString(),
                    userId = userId,
                    userName = userName,
                    message = message,
                    conversationId = conversationId ?? Guid.NewGuid().ToString(),
                    timestamp = DateTime.UtcNow,
                    type = "user_message"
                };
                
                // Send to user's personal group
                await Clients.Group($"user_{userId}").SendAsync("ReceiveMessage", messageObj);
                
                // Generate AI response
                var aiResponse = GenerateAIResponse(message, userId, userName);
                
                // Send AI response
                await Clients.Group($"user_{userId}").SendAsync("ReceiveMessage", aiResponse);
                
                _logger.LogInformation($"✅ Message and AI response sent to user {userId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending message from {Context.ConnectionId}");
                await Clients.Caller.SendAsync("Error", "Failed to send message");
            }
        }

        public async Task NotifyTyping(bool isTyping)
        {
            try
            {
                var userId = GetUserId();
                var userName = Context.User?.Identity?.Name ?? "Anonymous";
                
                await Clients.Others.SendAsync("UserTyping", new
                {
                    userId = userId,
                    userName = userName,
                    isTyping = isTyping,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error notifying typing status: {Context.ConnectionId}");
            }
        }

        private object GenerateAIResponse(string userMessage, string userId, string userName)
        {
            try
            {
                var responses = new[]
                {
                    $"Thank you, {userName}! Your insight about leadership is valuable. Let's explore how you can apply this in your daily leadership practice.",
                    $"Excellent reflection, {userName}! This shows strong self-awareness. How might you use this learning to mentor others?",
                    $"Great point, {userName}! Your leadership journey is unique. What specific action will you take based on this conversation?",
                    $"I appreciate your thoughtfulness, {userName}. This demonstrates growth mindset. Let's dive deeper into practical applications."
                };

                var random = new Random();
                var response = responses[random.Next(responses.Length)];

                return new
                {
                    id = Guid.NewGuid().ToString(),
                    userId = "ai_assistant",
                    userName = "TinkerGenie AI",
                    message = response,
                    timestamp = DateTime.UtcNow,
                    type = "ai_response",
                    isAI = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating AI response");
                return new
                {
                    id = Guid.NewGuid().ToString(),
                    userId = "ai_assistant",
                    userName = "TinkerGenie AI",
                    message = "I'm here to support your leadership development. How can I help you today?",
                    timestamp = DateTime.UtcNow,
                    type = "ai_response",
                    isAI = true
                };
            }
        }
    }
}
