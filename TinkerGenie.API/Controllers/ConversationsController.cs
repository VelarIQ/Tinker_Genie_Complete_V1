using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Npgsql;
using System.Data;

namespace TinkerGenie.API.Controllers
{
    [ApiController]
    [Route("api/conversations")]
    [Authorize]
    public class ConversationsController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<ConversationsController> _logger;
        private readonly string _connectionString;

        public ConversationsController(IConfiguration configuration, ILogger<ConversationsController> logger)
        {
            _configuration = configuration;
            _logger = logger;
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
        }

        // GET /api/conversations - list conversations for the user from conversation_threads
        [HttpGet]
        [HttpGet("recent")]
        public async Task<IActionResult> GetUserConversations([FromQuery] string? userId = null, [FromQuery] int limit = 50)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                var effectiveUserId = userId;
                if (string.IsNullOrWhiteSpace(effectiveUserId))
                {
                    effectiveUserId = User?.FindFirst("userId")?.Value ?? User?.FindFirst("sub")?.Value;
                }

                if (!Guid.TryParse(effectiveUserId ?? string.Empty, out var uid))
                {
                    return Ok(new { conversations = Array.Empty<object>(), activeConversationId = (string?)null });
                }

                var cmd = new NpgsqlCommand(@"
                    SELECT thread_id, type, title, status, updated_at
                    FROM conversation_threads
                    WHERE user_id = @uid
                    ORDER BY updated_at DESC
                    LIMIT @limit", conn);
                cmd.Parameters.AddWithValue("uid", uid);
                cmd.Parameters.AddWithValue("limit", limit);

                var conversations = new List<object>();
                string? activeConversationId = null;
                await using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var id = reader.GetGuid(0).ToString();
                        var type = reader.GetString(1);
                        var title = reader.IsDBNull(2) ? "Chat" : reader.GetString(2);
                        var status = reader.GetString(3);
                        var updated = reader.GetDateTime(4).ToString("O");
                        conversations.Add(new { id, title, type, status, updatedAt = updated });
                        if (activeConversationId == null) activeConversationId = id;
                    }
                }

                return Ok(new { conversations, activeConversationId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving conversations for user {UserId}", userId);
                return StatusCode(500, new { error = "Failed to retrieve conversations" });
            }
        }

        // Compatibility: POST /api/conversations/create
        [HttpPost("create")]
        public async Task<IActionResult> Create([FromBody] MessageRequest request)
        {
            var userId = User?.FindFirst("userId")?.Value ?? User?.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();
            return await SaveMessageInternal(userId, request);
        }

        // POST /api/conversations - create a new conversation thread
        [HttpPost]
        public async Task<IActionResult> CreateConversation([FromBody] ConversationCreateRequest request)
        {
            try
            {
                var userId = User?.FindFirst("userId")?.Value ?? User?.FindFirst("sub")?.Value;
                if (string.IsNullOrEmpty(userId)) return Unauthorized();

                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                var newId = Guid.NewGuid();
                var cmd = new NpgsqlCommand(@"
                    INSERT INTO conversation_threads
                    (thread_id, user_id, type, title, status, metadata, created_at, updated_at)
                    VALUES (@id::uuid, @userId::uuid, @type, @title, 'active', '{}'::jsonb, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
                    ON CONFLICT (thread_id) DO NOTHING", conn);
                cmd.Parameters.AddWithValue("id", newId);
                cmd.Parameters.AddWithValue("userId", Guid.Parse(userId));
                cmd.Parameters.AddWithValue("type", (object?)request?.Type ?? "general");
                cmd.Parameters.AddWithValue("title", (object?)request?.Title ?? "Chat");
                await cmd.ExecuteNonQueryAsync();

                return Ok(new { id = newId.ToString(), title = request?.Title ?? "Chat", type = request?.Type ?? "general" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating conversation for user {UserId}", User?.FindFirst("userId")?.Value);
                return StatusCode(500, new { error = "Failed to create conversation" });
            }
        }

        // GET /api/conversations/{conversationId}/messages - load messages for a thread from thread_messages
        [HttpGet("{conversationId}/messages")]
        public async Task<IActionResult> GetConversationMessages(string conversationId)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                var cmd = new NpgsqlCommand(@"
                    SELECT id, role, content, timestamp
                    FROM thread_messages
                    WHERE thread_id = @tid::uuid
                    ORDER BY timestamp ASC", conn);
                cmd.Parameters.AddWithValue("tid", Guid.Parse(conversationId));

                var messages = new List<object>();
                await using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        messages.Add(new
                        {
                            id = reader.GetInt64(0).ToString(),
                            sender = reader.GetString(1) == "assistant" ? "assistant" : "user",
                            content = reader.GetString(2),
                            timestamp = reader.GetDateTime(3).ToString("O")
                        });
                    }
                }

                return Ok(new { messages });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting messages for conversation {ConversationId}", conversationId);
                return StatusCode(500, new { error = "Failed to get messages" });
            }
        }

        [HttpPost("{userId}/messages")]
        public Task<IActionResult> SaveMessage(string userId, [FromBody] MessageRequest request)
            => SaveMessageInternal(userId, request);

        private async Task<IActionResult> SaveMessageInternal(string userId, MessageRequest request)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                
                // Parse conversation ID or create new one
                var conversationId = !string.IsNullOrEmpty(request.ConversationId) 
                    ? Guid.Parse(request.ConversationId)
                    : Guid.NewGuid();
                    
                // Ensure conversation exists
                var convCmd = new NpgsqlCommand(@"
                    INSERT INTO conversations 
                    (conversation_id, user_id, created_at, last_message_at)
                    VALUES (@conversationId::uuid, @userId::uuid, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
                    ON CONFLICT (conversation_id) 
                    DO UPDATE SET last_message_at = CURRENT_TIMESTAMP", conn);
                
                convCmd.Parameters.AddWithValue("conversationId", conversationId);
                convCmd.Parameters.AddWithValue("userId", Guid.Parse(userId));
                
                await convCmd.ExecuteNonQueryAsync();
                
                // Save message
                if (request.Role == "user")
                {
                    var msgCmd = new NpgsqlCommand(@"
                        INSERT INTO conversation_messages 
                        (conversation_id, user_id, user_message, ai_response, timestamp)
                        VALUES (@conversationId::uuid, @userId::uuid, @content, '', CURRENT_TIMESTAMP)", conn);
                    
                    msgCmd.Parameters.AddWithValue("conversationId", conversationId);
                    msgCmd.Parameters.AddWithValue("userId", Guid.Parse(userId));
                    msgCmd.Parameters.AddWithValue("content", request.Content ?? "");
                    
                    await msgCmd.ExecuteNonQueryAsync();
                }
                else if (request.Role == "assistant")
                {
                    // Update the most recent user message with AI response
                    var updateCmd = new NpgsqlCommand(@"
                        UPDATE conversation_messages 
                        SET ai_response = @content
                        WHERE conversation_id = @conversationId::uuid 
                        AND user_id = @userId::uuid
                        AND ai_response = ''
                        ORDER BY timestamp DESC
                        LIMIT 1", conn);
                    
                    updateCmd.Parameters.AddWithValue("conversationId", conversationId);
                    updateCmd.Parameters.AddWithValue("userId", Guid.Parse(userId));
                    updateCmd.Parameters.AddWithValue("content", request.Content ?? "");
                    
                    var updated = await updateCmd.ExecuteNonQueryAsync();
                    
                    // If no row was updated, insert as new message
                    if (updated == 0)
                    {
                        var msgCmd = new NpgsqlCommand(@"
                            INSERT INTO conversation_messages 
                            (conversation_id, user_id, user_message, ai_response, timestamp)
                            VALUES (@conversationId::uuid, @userId::uuid, '', @content, CURRENT_TIMESTAMP)", conn);
                        
                        msgCmd.Parameters.AddWithValue("conversationId", conversationId);
                        msgCmd.Parameters.AddWithValue("userId", Guid.Parse(userId));
                        msgCmd.Parameters.AddWithValue("content", request.Content ?? "");
                        
                        await msgCmd.ExecuteNonQueryAsync();
                    }
                }
                
                return Ok(new { success = true, conversationId = conversationId.ToString() });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving message for user {UserId}", userId);
                return StatusCode(500, new { error = "Failed to save message" });
            }
        }

        [HttpGet("{userId}/sync")]
        public async Task<IActionResult> SyncConversations(string userId, [FromQuery] string? lastSync)
        {
            try
            {
                var messages = new List<object>();
                
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                
                DateTime syncTime = DateTime.MinValue;
                if (!string.IsNullOrEmpty(lastSync))
                {
                    DateTime.TryParse(lastSync, out syncTime);
                }
                
                // Get messages since last sync
                var cmd = new NpgsqlCommand(@"
                    SELECT 
                        cm.conversation_id,
                        cm.user_message,
                        cm.ai_response,
                        cm.timestamp
                    FROM conversation_messages cm
                    WHERE cm.user_id = @userId::uuid
                    AND cm.timestamp > @lastSync
                    ORDER BY cm.timestamp DESC
                    LIMIT 100", conn);
                    
                cmd.Parameters.AddWithValue("userId", Guid.Parse(userId));
                cmd.Parameters.AddWithValue("lastSync", syncTime);
                
                await using var reader = await cmd.ExecuteReaderAsync();
                
                while (await reader.ReadAsync())
                {
                    var conversationId = reader.GetGuid(0).ToString();
                    var userMessage = reader.GetString(1);
                    var aiResponse = reader.GetString(2);
                    var timestamp = reader.GetDateTime(3);
                    
                    if (!string.IsNullOrEmpty(userMessage))
                    {
                        messages.Add(new
                        {
                            conversationId,
                            message = userMessage,
                            role = "user",
                            timestamp = timestamp.ToString("yyyy-MM-ddTHH:mm:ssZ")
                        });
                    }
                    
                    if (!string.IsNullOrEmpty(aiResponse))
                    {
                        messages.Add(new
                        {
                            conversationId,
                            message = aiResponse,
                            role = "assistant",
                            timestamp = timestamp.AddSeconds(1).ToString("yyyy-MM-ddTHH:mm:ssZ")
                        });
                    }
                }
                
                return Ok(messages);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing conversations for user {UserId}", userId);
                return StatusCode(500, new { error = "Failed to sync conversations" });
            }
        }

        [HttpDelete("{userId}/conversations/{conversationId}")]
        public async Task<IActionResult> DeleteConversation(string userId, string conversationId)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                
                // NOTE: Soft delete would require adding is_deleted column
                // For now, we'll actually delete the messages
                // TODO: Add is_deleted column and update to use soft delete for compliance
                var cmd = new NpgsqlCommand(@"
                    DELETE FROM conversation_threads
                    WHERE thread_id = @conversationId::uuid 
                    AND user_id = @userId::uuid", conn);
                
                cmd.Parameters.AddWithValue("conversationId", Guid.Parse(conversationId));
                cmd.Parameters.AddWithValue("userId", Guid.Parse(userId));
                
                var affected = await cmd.ExecuteNonQueryAsync();
                
                _logger.LogInformation("Soft deleted {Count} messages for conversation {ConversationId}", affected, conversationId);
                
                return Ok(new { success = true, deleted = affected });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting conversation {ConversationId} for user {UserId}", conversationId, userId);
                return StatusCode(500, new { error = "Failed to delete conversation" });
            }
        }
    }

    public class MessageRequest
    {
        public string? ConversationId { get; set; }
        public string? Content { get; set; }
        public string? Role { get; set; }
        public string? Timestamp { get; set; }
    }

    public class ConversationCreateRequest
    {
        public string? Title { get; set; }
        public string? Type { get; set; }
    }
}
