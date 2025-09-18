using TinkerGenie.API.Models;
using Npgsql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TinkerGenie.API.Services
{
    public interface IConversationService
    {
        Task<Guid> SaveConversation(string userId, string userMessage, string aiResponse, string? existingConversationId = null);
        Task<List<ConversationMessage>> GetConversationHistory(string userId, int limit = 10);
    }

    public class ConversationService : IConversationService
    {
        private readonly ILogger<ConversationService> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public ConversationService(ILogger<ConversationService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
        }

    public async Task<Guid> SaveConversation(string userId, string userMessage, string aiResponse, string? existingConversationId = null)
    {
        try
        {
            // Use existing conversation ID if provided, otherwise create new one
            var conversationId = !string.IsNullOrEmpty(existingConversationId) 
                ? Guid.Parse(existingConversationId) 
                : Guid.NewGuid();
                
            await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                // FIRST: Create or get conversation record (required for foreign key constraint)
                var convCmd = new NpgsqlCommand(@"
                    INSERT INTO conversations 
                    (conversation_id, user_id, created_at, last_message_at)
                    VALUES (@conversationId::uuid, @userId::uuid, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
                    ON CONFLICT (conversation_id) 
                    DO UPDATE SET last_message_at = CURRENT_TIMESTAMP", conn);
                
                convCmd.Parameters.AddWithValue("conversationId", conversationId);
                convCmd.Parameters.AddWithValue("userId", Guid.Parse(userId));
                
                await convCmd.ExecuteNonQueryAsync();

                // THEN: Save user message if provided
                if (!string.IsNullOrEmpty(userMessage))
                {
                    var userCmd = new NpgsqlCommand(@"
                        INSERT INTO conversation_messages 
                        (conversation_id, user_id, user_message, ai_response, timestamp)
                        VALUES (@conversationId::uuid, @userId::uuid, @userMessage, '', CURRENT_TIMESTAMP)", conn);
                    
                    userCmd.Parameters.AddWithValue("conversationId", conversationId);
                    userCmd.Parameters.AddWithValue("userId", Guid.Parse(userId));
                    userCmd.Parameters.AddWithValue("userMessage", userMessage);
                    
                    await userCmd.ExecuteNonQueryAsync();
                }

                // Save AI response if provided (could be combined with user message)
                if (!string.IsNullOrEmpty(aiResponse) && string.IsNullOrEmpty(userMessage))
                {
                    var aiCmd = new NpgsqlCommand(@"
                        INSERT INTO conversation_messages 
                        (conversation_id, user_id, user_message, ai_response, timestamp)
                        VALUES (@conversationId::uuid, @userId::uuid, '', @aiResponse, CURRENT_TIMESTAMP)", conn);
                    
                    aiCmd.Parameters.AddWithValue("conversationId", conversationId);
                    aiCmd.Parameters.AddWithValue("userId", Guid.Parse(userId));
                    aiCmd.Parameters.AddWithValue("aiResponse", aiResponse);
                    
                    await aiCmd.ExecuteNonQueryAsync();
                }

                // If we have both user message and AI response, save them together
                if (!string.IsNullOrEmpty(userMessage) && !string.IsNullOrEmpty(aiResponse))
                {
                    var bothCmd = new NpgsqlCommand(@"
                        UPDATE conversation_messages 
                        SET ai_response = @aiResponse
                        WHERE id = (
                            SELECT id FROM conversation_messages 
                            WHERE conversation_id = @conversationId::uuid 
                            AND user_id = @userId::uuid
                            AND user_message = @userMessage
                            AND ai_response = ''
                            ORDER BY timestamp DESC
                            LIMIT 1
                        )", conn);
                    
                    bothCmd.Parameters.AddWithValue("conversationId", conversationId);
                    bothCmd.Parameters.AddWithValue("userId", Guid.Parse(userId));
                    bothCmd.Parameters.AddWithValue("userMessage", userMessage);
                    bothCmd.Parameters.AddWithValue("aiResponse", aiResponse);
                    
                    await bothCmd.ExecuteNonQueryAsync();
                }

                _logger.LogInformation($"Saved conversation {conversationId} for user {userId}");
                return conversationId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving conversation for user {UserId}: {Message}", userId, ex.Message);
                return Guid.NewGuid(); // Return a new GUID even on error
            }
        }

        public async Task<List<ConversationMessage>> GetConversationHistory(string userId, int limit = 10)
        {
            var messages = new List<ConversationMessage>();
            
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                // Retrieve messages (TODO: Add is_deleted column for soft delete)
                var cmd = new NpgsqlCommand(@"
                    SELECT role, message, timestamp 
                    FROM conversation_messages 
                    WHERE user_id = @userId::uuid 
                    ORDER BY timestamp DESC 
                    LIMIT @limit", conn);
                
                cmd.Parameters.AddWithValue("userId", Guid.Parse(userId));
                cmd.Parameters.AddWithValue("limit", limit);

                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    messages.Add(new ConversationMessage
                    {
                        Role = reader.GetString(0),
                        Message = reader.GetString(1),
                        Timestamp = reader.GetDateTime(2)
                    });
                }
                
                // Reverse to get chronological order
                messages.Reverse();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting conversation history for user {UserId}", userId);
            }

            return messages;
        }
    }

    public class ConversationMessage
    {
        public string Role { get; set; } = "";
        public string Message { get; set; } = "";
        public DateTime Timestamp { get; set; }
    }
}