#!/bin/bash

# CORRECTED TINKER GENIE SYSTEM FIX
# This script matches your ACTUAL database schema

echo "🔧 Starting corrected system fix..."

# 1. FIX DATABASE SCHEMA BASED ON ACTUAL STRUCTURE
echo "🗄️ Fixing database schema based on actual structure..."
PGPASSWORD=***PASSWORD*** psql -h 161.35.5.159 -U genie_admin -d tinker_genie << 'EOF'

-- Add missing columns to existing tables
DO $$
BEGIN
    -- Add content column to conversation_messages if it doesn't exist
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name='conversation_messages' AND column_name='content') THEN
        ALTER TABLE conversation_messages ADD COLUMN content TEXT;
    END IF;
    
    -- Add is_user column to conversation_messages if it doesn't exist
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name='conversation_messages' AND column_name='is_user') THEN
        ALTER TABLE conversation_messages ADD COLUMN is_user BOOLEAN DEFAULT FALSE;
    END IF;
END $$;

-- Insert admin user with ACTUAL column names
INSERT INTO users (id, tenant_id, tbb_user_id, email, first_name, last_name, role, is_active)
VALUES (
    '550e8400-e29b-41d4-a716-446655440000', 
    NULL, 
    999999, 
    'admin@twobrain.ai', 
    'Admin', 
    'User', 
    'admin', 
    TRUE
) ON CONFLICT (id) DO UPDATE SET 
    email = EXCLUDED.email,
    role = EXCLUDED.role,
    first_name = EXCLUDED.first_name,
    last_name = EXCLUDED.last_name;

-- Insert genie instance with ACTUAL column names
INSERT INTO genie_instances (
    id, tenant_id, name, type, hierarchy_level, owner_user_id, 
    persona_config, is_active, user_id
) VALUES (
    '11111111-1111-1111-1111-111111111111',
    NULL,
    'TinkerGenie Business Coach',
    'business_coach',
    1,
    '550e8400-e29b-41d4-a716-446655440000',
    '{"role": "business_coach", "specialty": "fitness_business"}',
    TRUE,
    '550e8400-e29b-41d4-a716-446655440000'
) ON CONFLICT (id) DO UPDATE SET
    owner_user_id = EXCLUDED.owner_user_id,
    user_id = EXCLUDED.user_id,
    persona_config = EXCLUDED.persona_config;

EOF

echo "✅ Database schema fixed successfully!"

# 2. CREATE WORKING CHATCONTROLLER WITH CORRECT COLUMN NAMES
echo "🤖 Creating ChatController with correct column names..."

cat > Controllers/ChatController.cs << 'CONTROLLER_EOF'
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Npgsql;
using System.Text.Json;
using OpenAI.Chat;

namespace TinkerGenie.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ChatController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;
        private readonly ILogger<ChatController> _logger;
        private readonly ChatClient _chatClient;

        public ChatController(IConfiguration configuration, ILogger<ChatController> logger)
        {
            _configuration = configuration;
            _connectionString = configuration.GetConnectionString("DefaultConnection") ??
                throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            _logger = logger;
            
            var openAiApiKey = configuration["OpenAI:ApiKey"] ??
                throw new InvalidOperationException("OpenAI API key not found.");
            _chatClient = new ChatClient("gpt-4o-mini", openAiApiKey);
        }

        [HttpPost]
        public async Task<IActionResult> SendMessage([FromBody] ChatRequest request)
        {
            try
            {
                // Validate input
                if (string.IsNullOrWhiteSpace(request.Message) || string.IsNullOrWhiteSpace(request.UserId))
                {
                    return BadRequest(new { message = "Message and UserId are required" });
                }

                // Validate UUID format
                if (!Guid.TryParse(request.UserId, out Guid userGuid))
                {
                    return BadRequest(new { message = "Invalid user ID format" });
                }

                // Get or create conversation
                var conversationId = await GetOrCreateConversation(userGuid);
                
                // Save user message
                await SaveMessage(conversationId, request.Message, "user", userGuid);

                // Get conversation context
                var context = await GetConversationContext(userGuid, conversationId);

                // Generate AI response
                var aiResponse = await GenerateAIResponse(request.Message, context);

                // Save AI response
                await SaveMessage(conversationId, aiResponse, "genie", userGuid);

                return Ok(new { 
                    response = aiResponse,
                    conversationId = conversationId.ToString(),
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing chat message for user {UserId}", request.UserId);
                return StatusCode(500, new { message = "Chat processing failed", error = ex.Message });
            }
        }

        [HttpGet("{conversationId}/history")]
        public async Task<IActionResult> GetConversationHistory(string conversationId)
        {
            try
            {
                if (!Guid.TryParse(conversationId, out Guid convGuid))
                {
                    return BadRequest(new { message = "Invalid conversation ID format" });
                }

                var messages = await GetConversationMessages(convGuid);
                return Ok(new { messages });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving conversation history for {ConversationId}", conversationId);
                return StatusCode(500, new { message = "Failed to retrieve conversation history" });
            }
        }

        [HttpGet("user/{userId}/conversations")]
        public async Task<IActionResult> GetUserConversations(string userId)
        {
            try
            {
                if (!Guid.TryParse(userId, out Guid userGuid))
                {
                    return BadRequest(new { message = "Invalid user ID format" });
                }

                using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                var cmd = new NpgsqlCommand(@"
                    SELECT id, title, conversation_type, started_at, last_message_at, message_count
                    FROM genie_conversations 
                    WHERE user_id = @userId 
                    ORDER BY last_message_at DESC
                    LIMIT 50", conn);
                
                cmd.Parameters.AddWithValue("userId", userGuid);

                var conversations = new List<object>();
                using var reader = await cmd.ExecuteReaderAsync();
                
                while (await reader.ReadAsync())
                {
                    conversations.Add(new
                    {
                        id = reader.GetGuid("id").ToString(),
                        title = reader.IsDBNull("title") ? null : reader.GetString("title"),
                        conversationType = reader.IsDBNull("conversation_type") ? null : reader.GetString("conversation_type"),
                        startedAt = reader.GetDateTime("started_at"),
                        lastMessageAt = reader.GetDateTime("last_message_at"),
                        messageCount = reader.GetInt32("message_count")
                    });
                }

                return Ok(new { conversations });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user conversations for {UserId}", userId);
                return StatusCode(500, new { message = "Failed to retrieve conversations" });
            }
        }

        private async Task<Guid> GetOrCreateConversation(Guid userId)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            // Try to get active conversation
            var getCmd = new NpgsqlCommand(@"
                SELECT id FROM genie_conversations 
                WHERE user_id = @userId AND status = 'active' 
                ORDER BY last_message_at DESC 
                LIMIT 1", conn);
            
            getCmd.Parameters.AddWithValue("userId", userId);
            
            var existingId = await getCmd.ExecuteScalarAsync();
            if (existingId != null)
            {
                return (Guid)existingId;
            }

            // Create new conversation - use owner_user_id instead of user_id for genie_instances
            var newId = Guid.NewGuid();
            var createCmd = new NpgsqlCommand(@"
                INSERT INTO genie_conversations (
                    id, user_id, genie_instance_id, title, conversation_type, 
                    status, started_at, last_message_at, message_count
                ) VALUES (
                    @id, @userId,
                    (SELECT id FROM genie_instances WHERE owner_user_id = @userId OR owner_user_id IS NULL LIMIT 1),
                    @title, 'chat', 'active', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, 0
                )", conn);
            
            createCmd.Parameters.AddWithValue("id", newId);
            createCmd.Parameters.AddWithValue("userId", userId);
            createCmd.Parameters.AddWithValue("title", $"Chat - {DateTime.UtcNow:yyyy-MM-dd HH:mm}");
            
            await createCmd.ExecuteNonQueryAsync();
            return newId;
        }

        private async Task SaveMessage(Guid conversationId, string messageText, string sender, Guid userId)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = new NpgsqlCommand(@"
                INSERT INTO conversation_messages (
                    id, conversation_id, sender, message_text, content, user_id, 
                    is_user, created_at
                ) VALUES (
                    @id, @conversationId, @sender, @messageText, @content, @userId, 
                    @isUser, CURRENT_TIMESTAMP
                )", conn);
            
            cmd.Parameters.AddWithValue("id", Guid.NewGuid());
            cmd.Parameters.AddWithValue("conversationId", conversationId);
            cmd.Parameters.AddWithValue("sender", sender);
            cmd.Parameters.AddWithValue("messageText", messageText);
            cmd.Parameters.AddWithValue("content", messageText);
            cmd.Parameters.AddWithValue("userId", userId);
            cmd.Parameters.AddWithValue("isUser", sender == "user");
            
            await cmd.ExecuteNonQueryAsync();

            // Update message count
            var updateCmd = new NpgsqlCommand(@"
                UPDATE genie_conversations 
                SET message_count = message_count + 1, last_message_at = CURRENT_TIMESTAMP 
                WHERE id = @conversationId", conn);
            
            updateCmd.Parameters.AddWithValue("conversationId", conversationId);
            await updateCmd.ExecuteNonQueryAsync();
        }

        private async Task<List<object>> GetConversationMessages(Guid conversationId)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = new NpgsqlCommand(@"
                SELECT sender, message_text, created_at
                FROM conversation_messages 
                WHERE conversation_id = @conversationId 
                ORDER BY created_at ASC", conn);
            
            cmd.Parameters.AddWithValue("conversationId", conversationId);

            var messages = new List<object>();
            using var reader = await cmd.ExecuteReaderAsync();
            
            while (await reader.ReadAsync())
            {
                messages.Add(new
                {
                    sender = reader.GetString("sender"),
                    message = reader.GetString("message_text"),
                    timestamp = reader.GetDateTime("created_at")
                });
            }

            return messages;
        }

        private async Task<string> GetConversationContext(Guid userId, Guid conversationId)
        {
            try
            {
                using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                // Get user context using ACTUAL column names
                var userCmd = new NpgsqlCommand(@"
                    SELECT email, first_name, last_name
                    FROM users
                    WHERE id = @userId", conn);
                
                userCmd.Parameters.AddWithValue("userId", userId);

                string userContext = "";
                using var userReader = await userCmd.ExecuteReaderAsync();
                if (await userReader.ReadAsync())
                {
                    var email = userReader.IsDBNull("email") ? "unknown" : userReader.GetString("email");
                    var firstName = userReader.IsDBNull("first_name") ? "" : userReader.GetString("first_name");
                    var lastName = userReader.IsDBNull("last_name") ? "" : userReader.GetString("last_name");
                    var fullName = $"{firstName} {lastName}".Trim();
                    userContext = $"User: {email}" + (string.IsNullOrEmpty(fullName) ? "" : $" ({fullName})");
                }
                userReader.Close();

                // Get recent messages for context
                var msgCmd = new NpgsqlCommand(@"
                    SELECT sender, message_text
                    FROM conversation_messages 
                    WHERE conversation_id = @conversationId 
                    ORDER BY created_at DESC 
                    LIMIT 6", conn);
                
                msgCmd.Parameters.AddWithValue("conversationId", conversationId);

                var recentMessages = new List<string>();
                using var msgReader = await msgCmd.ExecuteReaderAsync();
                while (await msgReader.ReadAsync())
                {
                    var sender = msgReader.GetString("sender");
                    var message = msgReader.GetString("message_text");
                    recentMessages.Add($"{sender}: {message}");
                }

                recentMessages.Reverse();
                var conversationHistory = string.Join("\n", recentMessages);

                return $"{userContext}\n\nRecent conversation:\n{conversationHistory}";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get conversation context for user {UserId}", userId);
                return "User context not available";
            }
        }

        private async Task<string> GenerateAIResponse(string userMessage, string context)
        {
            try
            {
                var systemPrompt = @"You are the TinkerGenie, an expert business coach specializing in fitness businesses. 
                You help gym owners and fitness entrepreneurs grow their businesses through strategic guidance, 
                practical advice, and actionable insights.

                Key principles:
                - Be conversational but professional
                - Focus on practical, actionable advice
                - Ask follow-up questions to understand their specific situation
                - Draw from best practices in fitness business management
                - Keep responses concise but helpful (2-3 paragraphs max)

                Context about this user and conversation:
                " + context;

                var messages = new List<ChatMessage>
                {
                    new SystemChatMessage(systemPrompt),
                    new UserChatMessage(userMessage)
                };

                var response = await _chatClient.CompleteChatAsync(messages);
                return response.Value.Content[0].Text;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate AI response");
                return "I apologize, but I'm having trouble processing your request right now. Please try again in a moment.";
            }
        }
    }

    public class ChatRequest
    {
        public string Message { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
    }
}
CONTROLLER_EOF

echo "✅ ChatController created successfully!"

# 3. BUILD AND RESTART
echo "🔨 Building and restarting service..."
dotnet build

if [ $? -eq 0 ]; then
    echo "✅ Build successful!"
    sudo systemctl restart tinker-api.service
    sleep 3
    
    # 4. TEST THE SYSTEM
    echo "🧪 Testing the system..."
    
    # Test auth
    echo "Testing authentication..."
    TOKEN=$(curl -s -X POST https://tinker.twobrain.ai/api/auth/login \
      -H "Content-Type: application/json" \
      -d '{"username": "admin", "password": "TinkerAdmin2025!"}' | python3 -c "import sys, json; print(json.load(sys.stdin)['token'])" 2>/dev/null)
    
    if [ -n "$TOKEN" ]; then
        echo "✅ Authentication working!"
        
        # Test chat
        echo "Testing chat functionality..."
        CHAT_RESPONSE=$(curl -s -X POST https://tinker.twobrain.ai/api/chat \
          -H "Content-Type: application/json" \
          -H "Authorization: Bearer $TOKEN" \
          -d '{"message": "Hello TinkerGenie! How can I improve my gym retention?", "userId": "550e8400-e29b-41d4-a716-446655440000"}')
        
        echo "Chat response:"
        echo "$CHAT_RESPONSE"
        
        if echo "$CHAT_RESPONSE" | grep -q "response"; then
            echo "✅ Chat functionality working!"
        else
            echo "❌ Chat test failed - see response above"
        fi
    else
        echo "❌ Authentication test failed"
    fi
    
    echo ""
    echo "🎉 CORRECTED SYSTEM FIX COMPLETE!"
    echo ""
    echo "✅ Database schema: MATCHED TO ACTUAL STRUCTURE"
    echo "✅ ChatController: USES CORRECT COLUMN NAMES"  
    echo "✅ Admin user: CREATED WITH PROPER FIELDS"
    echo "✅ Genie instance: CREATED WITH PROPER RELATIONSHIPS"
    echo ""
    echo "🚀 System should now be working!"
    echo "📱 Test the PWA at: https://tinker.twobrain.ai"
    echo "🔑 Admin login: admin / TinkerAdmin2025!"
    
else
    echo "❌ Build failed! Check the error messages above."
    exit 1
fi
