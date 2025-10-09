using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Npgsql;
using System.Security.Claims;
using TinkerGenie.API.Services;

namespace TinkerGenie.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ConversationsController : ControllerBase
    {
        private readonly ILogger<ConversationsController> _logger;
        private readonly string _connectionString;
        private readonly ISessionManagerService _sessionManager;

        public ConversationsController(
            ILogger<ConversationsController> logger,
            IConfiguration configuration,
            ISessionManagerService sessionManager)
        {
            _logger = logger;
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
            _sessionManager = sessionManager;
        }

        private async Task<string> ResolveCanonicalUserId(ClaimsPrincipal user)
        {
            // IMPORTANT: Check "user_id" claim FIRST as it contains the actual GUID
            var userId = user.FindFirst("user_id")?.Value 
                ?? user.FindFirst("userId")?.Value 
                ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("UserId not found in claims.");
                throw new UnauthorizedAccessException("User ID not found.");
            }

            return userId;
        }

        [HttpGet]
        public async Task<IActionResult> GetConversations([FromQuery] int page = 1, [FromQuery] int limit = 20)
        {
            try
            {
                var userId = await ResolveCanonicalUserId(User);
                
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                
                var offset = (page - 1) * limit;
                
                // Get total count
                var countCmd = new NpgsqlCommand(@"
                    SELECT COUNT(DISTINCT thread_id) 
                    FROM conversation_threads 
                    WHERE user_id = @userId::uuid", conn);
                countCmd.Parameters.AddWithValue("userId", userId);
                var totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync() ?? 0);
                
                // Get conversations with pagination
                var cmd = new NpgsqlCommand(@"
                    SELECT DISTINCT 
                        ct.thread_id::text as id,
                        ct.title as title,
                        ct.type as type,
                        ct.created_at as created_at,
                        ct.updated_at as updated_at,
                        CASE WHEN ct.status = 'active' THEN true ELSE false END as is_active,
                        (SELECT content FROM thread_messages 
                         WHERE thread_id = ct.thread_id AND role = 'assistant'
                         ORDER BY created_at DESC LIMIT 1) as last_message,
                        (SELECT created_at FROM thread_messages 
                         WHERE thread_id = ct.thread_id 
                         ORDER BY created_at DESC LIMIT 1) as last_message_time,
                        (SELECT COUNT(*) FROM thread_messages WHERE thread_id = ct.thread_id) as message_count
                    FROM conversation_threads ct
                    WHERE ct.user_id = @userId::uuid
                    ORDER BY ct.updated_at DESC NULLS LAST, ct.created_at DESC
                    LIMIT @limit OFFSET @offset", conn);
                    
                cmd.Parameters.AddWithValue("userId", userId);
                cmd.Parameters.AddWithValue("limit", limit);
                cmd.Parameters.AddWithValue("offset", offset);
                
                var conversations = new List<object>();
                await using var reader = await cmd.ExecuteReaderAsync();
                
                while (await reader.ReadAsync())
                {
                    conversations.Add(new {
                        id = reader.GetString(0),
                        title = reader.IsDBNull(1) ? GenerateTitle(reader.GetString(2)) : reader.GetString(1),
                        type = reader.IsDBNull(2) ? "general" : reader.GetString(2),
                        createdAt = reader.GetDateTime(3),
                        updatedAt = reader.IsDBNull(4) ? reader.GetDateTime(3) : reader.GetDateTime(4),
                        isActive = reader.IsDBNull(5) || reader.GetBoolean(5),
                        preview = reader.IsDBNull(6) ? "No messages" : TruncateMessage(reader.GetString(6)),
                        lastMessage = reader.IsDBNull(7) ? reader.GetDateTime(3) : reader.GetDateTime(7),
                        timestamp = reader.IsDBNull(7) ? reader.GetDateTime(3) : reader.GetDateTime(7),
                        messageCount = reader.IsDBNull(8) ? 0 : reader.GetInt32(8)
                    });
                }

                _logger.LogInformation("Retrieved {Count} conversations for user {UserId}", conversations.Count, userId);

                return Ok(new { 
                    conversations = conversations,
                    activeConversationId = conversations.FirstOrDefault() != null ? 
                        ((dynamic)conversations.First()).id : null,
                    pagination = new {
                        page = page,
                        limit = limit,
                        totalCount = totalCount,
                        totalPages = (int)Math.Ceiling((double)totalCount / limit)
                    }
                });
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized(new { message = "User not authenticated" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving conversations");
                return Ok(new { 
                    conversations = new object[] { }, 
                    activeConversationId = (string?)null,
                    pagination = new { page = 1, limit = limit, totalCount = 0, totalPages = 0 }
                });
            }
        }

        [HttpGet("{conversationId}")]
        public async Task<IActionResult> GetConversation(string conversationId)
        {
            try
            {
                var userId = await ResolveCanonicalUserId(User);
                
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                
                // Verify ownership
                var verifyCmd = new NpgsqlCommand(
                    "SELECT COUNT(*) FROM conversation_threads WHERE thread_id = @threadId::uuid AND user_id = @userId::uuid", 
                    conn);
                verifyCmd.Parameters.AddWithValue("threadId", conversationId);
                verifyCmd.Parameters.AddWithValue("userId", userId);
                
                if (Convert.ToInt32(await verifyCmd.ExecuteScalarAsync()) == 0)
                {
                    return NotFound(new { message = "Conversation not found" });
                }
                
                // Get conversation details
                var cmd = new NpgsqlCommand(@"
                    SELECT 
                        ct.thread_id,
                        ct.thread_title,
                        ct.conversation_type,
                        ct.created_at,
                        ct.updated_at,
                        ct.is_active,
                        ct.metadata
                    FROM conversation_threads ct
                    WHERE ct.thread_id = @threadId::uuid", conn);
                    
                cmd.Parameters.AddWithValue("threadId", conversationId);
                
                await using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var conversation = new {
                        id = reader.GetString(0),
                        title = reader.IsDBNull(1) ? "Untitled" : reader.GetString(1),
                        type = reader.IsDBNull(2) ? "general" : reader.GetString(2),
                        createdAt = reader.GetDateTime(3),
                        updatedAt = reader.IsDBNull(4) ? reader.GetDateTime(3) : reader.GetDateTime(4),
                        isActive = reader.IsDBNull(5) || reader.GetBoolean(5),
                        metadata = reader.IsDBNull(6) ? null : reader.GetFieldValue<string>(6)
                    };
                    
                    return Ok(conversation);
                }
                
                return NotFound(new { message = "Conversation not found" });
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized(new { message = "User not authenticated" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving conversation {ConversationId}", conversationId);
                return StatusCode(500, new { message = "Failed to retrieve conversation" });
            }
        }

        [HttpGet("{conversationId}/messages")]
        public async Task<IActionResult> GetConversationMessages(string conversationId)
        {
            try
            {
                var userId = await ResolveCanonicalUserId(User);
                
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                
                // Verify ownership
                var verifyCmd = new NpgsqlCommand(
                    "SELECT COUNT(*) FROM conversation_threads WHERE thread_id = @threadId::uuid AND user_id = @userId::uuid", 
                    conn);
                verifyCmd.Parameters.AddWithValue("threadId", conversationId);
                verifyCmd.Parameters.AddWithValue("userId", userId);
                
                if (Convert.ToInt32(await verifyCmd.ExecuteScalarAsync()) == 0)
                {
                    return NotFound(new { message = "Conversation not found" });
                }
                
                // Get messages for this conversation
                var cmd = new NpgsqlCommand(@"
                    SELECT id, role, content, timestamp
                    FROM thread_messages
                    WHERE thread_id = @threadId::uuid
                    ORDER BY timestamp ASC", conn);
                    
                cmd.Parameters.AddWithValue("threadId", conversationId);
                
                var messages = new List<object>();
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    messages.Add(new {
                        id = reader.GetGuid(0).ToString(),
                        role = reader.GetString(1),
                        content = reader.GetString(2),
                        timestamp = reader.GetDateTime(3)
                    });
                }
                
                return Ok(messages);
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized(new { message = "User not authenticated" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving messages for conversation {ConversationId}", conversationId);
                return StatusCode(500, new { message = "Failed to retrieve messages" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateConversation([FromBody] CreateConversationRequest request)
        {
            try
            {
                var userId = await ResolveCanonicalUserId(User);
                
                var conversationType = request.Type ?? "general";
                var thread = await _sessionManager.CreateThread(
                    userId, 
                    ParseConversationType(conversationType), 
                    request.Title ?? GenerateTitle(conversationType)
                );

                _logger.LogInformation("Created conversation {ConversationId} for user {UserId}", thread.ThreadId, userId);

                return Ok(new {
                    id = thread.ThreadId,
                    title = thread.Title,
                    type = conversationType,
                    createdAt = DateTime.UtcNow,
                    isActive = true
                });
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized(new { message = "User not authenticated" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating conversation");
                return StatusCode(500, new { message = "Failed to create conversation" });
            }
        }

        [HttpDelete("{conversationId}")]
        public async Task<IActionResult> DeleteConversation(string conversationId)
        {
            try
            {
                var userId = await ResolveCanonicalUserId(User);
                
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                
                // Verify ownership before deletion
                var verifyCmd = new NpgsqlCommand(
                    "SELECT COUNT(*) FROM conversation_threads WHERE thread_id = @threadId::uuid AND user_id = @userId::uuid", 
                    conn);
                verifyCmd.Parameters.AddWithValue("threadId", conversationId);
                verifyCmd.Parameters.AddWithValue("userId", userId);
                
                if (Convert.ToInt32(await verifyCmd.ExecuteScalarAsync()) == 0)
                {
                    return NotFound(new { message = "Conversation not found" });
                }
                
                // Soft delete by marking as inactive
                var deleteCmd = new NpgsqlCommand(
                    "UPDATE conversation_threads SET is_active = false, updated_at = NOW() WHERE thread_id = @threadId::uuid", 
                    conn);
                deleteCmd.Parameters.AddWithValue("threadId", conversationId);
                await deleteCmd.ExecuteNonQueryAsync();

                _logger.LogInformation("Deleted conversation {ConversationId} for user {UserId}", conversationId, userId);

                return Ok(new { message = "Conversation deleted successfully" });
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized(new { message = "User not authenticated" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting conversation {ConversationId}", conversationId);
                return StatusCode(500, new { message = "Failed to delete conversation" });
            }
        }

        [HttpPut("{conversationId}/title")]
        public async Task<IActionResult> UpdateConversationTitle(string conversationId, [FromBody] UpdateTitleRequest request)
        {
            try
            {
                var userId = await ResolveCanonicalUserId(User);
                
                if (string.IsNullOrWhiteSpace(request.Title))
                {
                    return BadRequest(new { message = "Title cannot be empty" });
                }
                
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                
                var updateCmd = new NpgsqlCommand(@"
                    UPDATE conversation_threads 
                    SET thread_title = @title, updated_at = NOW() 
                    WHERE thread_id = @threadId::uuid AND user_id = @userId::uuid", conn);
                    
                updateCmd.Parameters.AddWithValue("title", request.Title);
                updateCmd.Parameters.AddWithValue("threadId", conversationId);
                updateCmd.Parameters.AddWithValue("userId", userId);
                
                var rowsAffected = await updateCmd.ExecuteNonQueryAsync();
                
                if (rowsAffected == 0)
                {
                    return NotFound(new { message = "Conversation not found" });
                }

                return Ok(new { message = "Title updated successfully", title = request.Title });
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized(new { message = "User not authenticated" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating conversation title");
                return StatusCode(500, new { message = "Failed to update title" });
            }
        }

        private string GenerateTitle(string conversationType)
        {
            return conversationType?.ToLower() switch
            {
                "daily_prompt" => $"Daily Reflection - {DateTime.Now:MMM dd}",
                "burning_fire" => $"Urgent Issue - {DateTime.Now:HH:mm}",
                "tinker_level" => $"Tinker Challenge - {DateTime.Now:MMM dd}",
                _ => $"Chat - {DateTime.Now:MMM dd HH:mm}"
            };
        }

        private string TruncateMessage(string message, int maxLength = 100)
        {
            if (string.IsNullOrWhiteSpace(message)) return "No messages";
            if (message.Length <= maxLength) return message;
            
            var truncated = message.Substring(0, maxLength);
            var lastSpace = truncated.LastIndexOf(' ');
            if (lastSpace > 0) truncated = truncated.Substring(0, lastSpace);
            
            return truncated + "...";
        }

        private ConversationType ParseConversationType(string type)
        {
            return type?.ToLower() switch
            {
                "daily_prompt" => ConversationType.DAILY_PROMPT,
                "burning_fire" => ConversationType.BURNING_FIRE,
                "tinker_level" => ConversationType.TINKER_LEVEL,
                _ => ConversationType.GENERAL_CHAT
            };
        }
    }

    public class CreateConversationRequest
    {
        public string? Title { get; set; }
        public string? Type { get; set; }
    }

    public class UpdateTitleRequest
    {
        public string Title { get; set; } = "";
    }
}
