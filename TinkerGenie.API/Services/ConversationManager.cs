using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Npgsql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TinkerGenie.API.Services
{
    public class ConversationManager
    {
        private readonly ILogger<ConversationManager> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;
        private readonly string _encryptionKey;
        
        // Conversation limits
        private const int MAX_MESSAGES_PER_CONVERSATION = 100;
        private const int WARNING_THRESHOLD = 80; // Warn at 80% capacity
        private const int MAX_CONVERSATION_SIZE_BYTES = 1_000_000; // 1MB compressed
        
        public ConversationManager(ILogger<ConversationManager> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
            // Use a proper key management system in production
            _encryptionKey = configuration["Encryption:Key"] ?? "TinkerGenie2025EncryptionKey123";
        }
        
        public class ConversationData
        {
            public string ConversationId { get; set; } = Guid.NewGuid().ToString();
            public string UserId { get; set; } = "";
            public List<Message> Messages { get; set; } = new();
            public DateTime StartedAt { get; set; } = DateTime.UtcNow;
            public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;
            public int MessageCount => Messages.Count;
            public bool IsNearLimit => MessageCount >= WARNING_THRESHOLD;
            public bool IsAtLimit => MessageCount >= MAX_MESSAGES_PER_CONVERSATION;
            public string Status { get; set; } = "active"; // active, archived, compressed
        }
        
        public class Message
        {
            public string Role { get; set; } = "";
            public string Content { get; set; } = "";
            public DateTime Timestamp { get; set; }
            public string MessageId { get; set; } = Guid.NewGuid().ToString();
        }
        
        public class ConversationStatus
        {
            public string ConversationId { get; set; } = "";
            public int MessageCount { get; set; }
            public int MaxMessages { get; set; } = MAX_MESSAGES_PER_CONVERSATION;
            public bool NeedsNewConversation { get; set; }
            public string WarningMessage { get; set; } = "";
        }
        
        public async Task<ConversationStatus> AddMessage(string userId, string conversationId, string role, string content)
        {
            try
            {
                // Get or create conversation
                var conversation = await GetActiveConversation(userId, conversationId);
                
                // Check if we need a new conversation
                if (conversation.IsAtLimit)
                {
                    // Archive current conversation
                    await ArchiveConversation(conversation);
                    
                    // Create new conversation
                    conversation = new ConversationData
                    {
                        UserId = userId,
                        ConversationId = Guid.NewGuid().ToString()
                    };
                    
                    return new ConversationStatus
                    {
                        ConversationId = conversation.ConversationId,
                        MessageCount = 0,
                        NeedsNewConversation = true,
                        WarningMessage = "Previous conversation archived. Starting new conversation."
                    };
                }
                
                // Add message
                conversation.Messages.Add(new Message
                {
                    Role = role,
                    Content = content,
                    Timestamp = DateTime.UtcNow
                });
                conversation.LastActivityAt = DateTime.UtcNow;
                
                // Save conversation
                await SaveConversation(conversation);
                
                // Prepare status
                var status = new ConversationStatus
                {
                    ConversationId = conversation.ConversationId,
                    MessageCount = conversation.MessageCount,
                    NeedsNewConversation = false
                };
                
                // Add warning if approaching limit
                if (conversation.MessageCount >= WARNING_THRESHOLD)
                {
                    var remaining = MAX_MESSAGES_PER_CONVERSATION - conversation.MessageCount;
                    status.WarningMessage = $"⚠️ Approaching conversation limit. {remaining} messages remaining before a new conversation starts.";
                }
                else if (conversation.MessageCount == MAX_MESSAGES_PER_CONVERSATION - 10)
                {
                    status.WarningMessage = "📝 Only 10 messages left in this conversation.";
                }
                
                return status;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding message to conversation");
                throw;
            }
        }
        
        private async Task<ConversationData> GetActiveConversation(string userId, string? conversationId)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            
            // If conversationId provided, try to load it
            if (!string.IsNullOrEmpty(conversationId))
            {
                var cmd = new NpgsqlCommand(@"
                    SELECT conversation_data 
                    FROM conversations 
                    WHERE conversation_id = @conversationId 
                      AND user_id = @userId::uuid
                      AND status = 'active'", conn);
                
                cmd.Parameters.AddWithValue("conversationId", conversationId);
                cmd.Parameters.AddWithValue("userId", Guid.Parse(userId));
                
                var encryptedData = await cmd.ExecuteScalarAsync() as byte[];
                if (encryptedData != null)
                {
                    var decrypted = DecryptData(encryptedData);
                    var decompressed = DecompressData(decrypted);
                    return JsonSerializer.Deserialize<ConversationData>(decompressed) ?? new ConversationData { UserId = userId };
                }
            }
            
            // No conversation found or no ID provided - get most recent active conversation
            var getRecentCmd = new NpgsqlCommand(@"
                SELECT conversation_id, conversation_data 
                FROM conversations 
                WHERE user_id = @userId::uuid
                  AND status = 'active'
                ORDER BY last_activity_at DESC
                LIMIT 1", conn);
            
            getRecentCmd.Parameters.AddWithValue("userId", Guid.Parse(userId));
            
            await using var reader = await getRecentCmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var encryptedData = reader.GetFieldValue<byte[]>(1);
                var decrypted = DecryptData(encryptedData);
                var decompressed = DecompressData(decrypted);
                return JsonSerializer.Deserialize<ConversationData>(decompressed) ?? new ConversationData { UserId = userId };
            }
            
            // No active conversation - create new one
            return new ConversationData
            {
                UserId = userId,
                ConversationId = Guid.NewGuid().ToString()
            };
        }
        
        private async Task SaveConversation(ConversationData conversation)
        {
            // Serialize, compress, and encrypt
            var json = JsonSerializer.Serialize(conversation);
            var compressed = CompressData(json);
            var encrypted = EncryptData(compressed);
            
            // Check size
            if (encrypted.Length > MAX_CONVERSATION_SIZE_BYTES)
            {
                _logger.LogWarning("Conversation {ConversationId} exceeds size limit", conversation.ConversationId);
                // Could implement additional compression or splitting logic here
            }
            
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            
            var cmd = new NpgsqlCommand(@"
                INSERT INTO conversations 
                (conversation_id, user_id, conversation_data, message_count, status, started_at, last_activity_at, compressed_size)
                VALUES (@conversationId, @userId::uuid, @data, @messageCount, @status, @startedAt, @lastActivityAt, @size)
                ON CONFLICT (conversation_id) 
                DO UPDATE SET 
                    conversation_data = @data,
                    message_count = @messageCount,
                    last_activity_at = @lastActivityAt,
                    compressed_size = @size", conn);
            
            cmd.Parameters.AddWithValue("conversationId", conversation.ConversationId);
            cmd.Parameters.AddWithValue("userId", Guid.Parse(conversation.UserId));
            cmd.Parameters.AddWithValue("data", encrypted);
            cmd.Parameters.AddWithValue("messageCount", conversation.MessageCount);
            cmd.Parameters.AddWithValue("status", conversation.Status);
            cmd.Parameters.AddWithValue("startedAt", conversation.StartedAt);
            cmd.Parameters.AddWithValue("lastActivityAt", conversation.LastActivityAt);
            cmd.Parameters.AddWithValue("size", encrypted.Length);
            
            await cmd.ExecuteNonQueryAsync();
            
            _logger.LogInformation("Saved conversation {ConversationId} with {MessageCount} messages ({Size} bytes compressed)", 
                conversation.ConversationId, conversation.MessageCount, encrypted.Length);
        }
        
        private async Task ArchiveConversation(ConversationData conversation)
        {
            conversation.Status = "archived";
            await SaveConversation(conversation);
            
            _logger.LogInformation("Archived conversation {ConversationId} with {MessageCount} messages", 
                conversation.ConversationId, conversation.MessageCount);
        }
        
        public async Task<List<ConversationSummary>> GetUserConversations(string userId, int limit = 10)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            
            var cmd = new NpgsqlCommand(@"
                SELECT 
                    conversation_id, 
                    message_count, 
                    status, 
                    started_at, 
                    last_activity_at, 
                    compressed_size
                FROM conversations 
                WHERE user_id = @userId::uuid
                ORDER BY last_activity_at DESC
                LIMIT @limit", conn);
            
            cmd.Parameters.AddWithValue("userId", Guid.Parse(userId));
            cmd.Parameters.AddWithValue("limit", limit);
            
            var summaries = new List<ConversationSummary>();
            
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                summaries.Add(new ConversationSummary
                {
                    ConversationId = reader.GetString(0),
                    MessageCount = reader.GetInt32(1),
                    Status = reader.GetString(2),
                    StartedAt = reader.GetDateTime(3),
                    LastActivityAt = reader.GetDateTime(4),
                    CompressedSize = reader.GetInt32(5)
                });
            }
            
            return summaries;
        }
        
        public class ConversationSummary
        {
            public string ConversationId { get; set; } = "";
            public int MessageCount { get; set; }
            public string Status { get; set; } = "";
            public DateTime StartedAt { get; set; }
            public DateTime LastActivityAt { get; set; }
            public int CompressedSize { get; set; }
            public string DisplayDate => FormatDate(LastActivityAt);
            
            private string FormatDate(DateTime date)
            {
                var now = DateTime.UtcNow;
                var diff = now - date;
                
                if (diff.TotalMinutes < 1) return "Just now";
                if (diff.TotalHours < 1) return $"{(int)diff.TotalMinutes} minutes ago";
                if (diff.TotalDays < 1) return $"{(int)diff.TotalHours} hours ago";
                if (diff.TotalDays < 2) return "Yesterday";
                if (diff.TotalDays < 7) return $"{(int)diff.TotalDays} days ago";
                
                return date.ToString("MMM d, yyyy");
            }
        }
        
        // Compression helpers
        private byte[] CompressData(string data)
        {
            var bytes = Encoding.UTF8.GetBytes(data);
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
            {
                gzip.Write(bytes, 0, bytes.Length);
            }
            return output.ToArray();
        }
        
        private string DecompressData(byte[] data)
        {
            using var input = new MemoryStream(data);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            return Encoding.UTF8.GetString(output.ToArray());
        }
        
        // Encryption helpers (use proper key management in production!)
        private byte[] EncryptData(byte[] data)
        {
            using var aes = Aes.Create();
            // Derive a proper key from the configured key
            using var sha256 = SHA256.Create();
            aes.Key = sha256.ComputeHash(Encoding.UTF8.GetBytes(_encryptionKey));
            aes.GenerateIV();
            
            using var encryptor = aes.CreateEncryptor();
            var encrypted = encryptor.TransformFinalBlock(data, 0, data.Length);
            
            // Combine IV and encrypted data
            var result = new byte[aes.IV.Length + encrypted.Length];
            Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
            Buffer.BlockCopy(encrypted, 0, result, aes.IV.Length, encrypted.Length);
            
            return result;
        }
        
        private byte[] DecryptData(byte[] data)
        {
            using var aes = Aes.Create();
            using var sha256 = SHA256.Create();
            aes.Key = sha256.ComputeHash(Encoding.UTF8.GetBytes(_encryptionKey));
            
            // Extract IV
            var iv = new byte[aes.IV.Length];
            Buffer.BlockCopy(data, 0, iv, 0, iv.Length);
            aes.IV = iv;
            
            // Extract encrypted data
            var encrypted = new byte[data.Length - iv.Length];
            Buffer.BlockCopy(data, iv.Length, encrypted, 0, encrypted.Length);
            
            using var decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
        }
    }
}



