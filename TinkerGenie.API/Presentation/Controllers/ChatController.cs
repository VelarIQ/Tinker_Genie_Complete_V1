using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using TinkerGenie.API.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace TinkerGenie.API.Presentation.Controllers
{
    [ApiController]
    [Route("api/chat")]
    [Authorize]
    public class ChatController : ControllerBase
    {
        private readonly ILogger<ChatController> _logger;
        private readonly IUserRepository _userRepository;

        public ChatController(ILogger<ChatController> logger, IUserRepository userRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        }

        private string GetUserId()
        {
            try
            {
                var userId = User.FindFirst("user_id")?.Value 
                    ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? User.FindFirst("sub")?.Value;
                
                if (string.IsNullOrEmpty(userId))
                {
                    _logger.LogWarning("No user ID found in JWT claims");
                    throw new UnauthorizedAccessException("User ID not found in token");
                }
                
                _logger.LogInformation($"Extracted user ID: {userId}");
                return userId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting user ID from JWT");
                throw;
            }
        }

        [HttpGet("daily-prompt")]
        public async Task<IActionResult> GetDailyPrompt()
        {
            try
            {
                _logger.LogInformation("🎯 Daily prompt requested");
                var userId = GetUserId();
                
                var prompt = new
                {
                    id = Guid.NewGuid().ToString(),
                    userId = userId,
                    prompt = "Today's leadership challenge: How can you inspire your team to embrace change and innovation?",
                    type = "daily_prompt",
                    timestamp = DateTime.UtcNow,
                    completed = false
                };

                _logger.LogInformation($"✅ Daily prompt generated for user: {userId}");
                return Ok(prompt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating daily prompt");
                return StatusCode(500, new { message = "Internal server error" });
            }
        }

        [HttpGet("conversations")]
        public async Task<IActionResult> GetConversations()
        {
            try
            {
                _logger.LogInformation("📋 Conversations requested");
                var userId = GetUserId();
                
                var conversations = new[]
                {
                    new
                    {
                        id = Guid.NewGuid().ToString(),
                        userId = userId,
                        title = "Daily Leadership Reflection",
                        lastMessage = "How can you inspire your team today?",
                        timestamp = DateTime.UtcNow.AddHours(-2),
                        type = "daily_prompt",
                        messageCount = 5
                    },
                    new
                    {
                        id = Guid.NewGuid().ToString(),
                        userId = userId,
                        title = "Leadership Challenge",
                        lastMessage = "Strategic thinking exercise...",
                        timestamp = DateTime.UtcNow.AddMinutes(-30),
                        type = "general_chat",
                        messageCount = 3
                    }
                };

                _logger.LogInformation($"✅ {conversations.Length} conversations returned for user: {userId}");
                return Ok(conversations);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching conversations");
                return StatusCode(500, new { message = "Internal server error" });
            }
        }

        [HttpPost("send-message")]
        public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest request)
        {
            try
            {
                _logger.LogInformation($"💬 Message received: {request?.Message?.Substring(0, Math.Min(50, request?.Message?.Length ?? 0))}...");
                var userId = GetUserId();
                
                if (request == null || string.IsNullOrWhiteSpace(request.Message))
                {
                    return BadRequest(new { message = "Message is required" });
                }

                var response = GenerateAIResponse(request.Message, userId);
                
                var chatResponse = new
                {
                    id = Guid.NewGuid().ToString(),
                    userId = userId,
                    userMessage = request.Message,
                    aiResponse = response,
                    conversationId = request.ConversationId ?? Guid.NewGuid().ToString(),
                    timestamp = DateTime.UtcNow,
                    type = "chat_response"
                };

                _logger.LogInformation($"✅ AI response generated for user: {userId}");
                return Ok(chatResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message");
                return StatusCode(500, new { message = "Internal server error" });
            }
        }

        private string GenerateAIResponse(string userMessage, string userId)
        {
            try
            {
                var responses = new[]
                {
                    "Thank you for sharing that insight! Your leadership perspective shows thoughtful consideration.",
                    "That's an excellent point about leadership. Your approach demonstrates strategic thinking.",
                    "I appreciate your reflection on this leadership challenge. This shows growth mindset.",
                    "Your leadership journey is unique. Let's explore practical applications of this insight."
                };

                var random = new Random();
                var baseResponse = responses[random.Next(responses.Length)];
                
                return $"{baseResponse} What specific action will you take based on this conversation?";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating AI response");
                return "Thank you for your message. I'm here to support your leadership development journey.";
            }
        }
    }

    public class SendMessageRequest
    {
        public string Message { get; set; } = string.Empty;
        public string? ConversationId { get; set; }
    }
}
