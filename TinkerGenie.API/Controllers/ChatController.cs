using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using TinkerGenie.API.Services;
using System.Text.Json;
using StackExchange.Redis;
using Npgsql;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using ConversationType = TinkerGenie.API.Services.ConversationType;

namespace TinkerGenie.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ChatController : ControllerBase
    {
        private readonly ILogger<ChatController> _logger;
        private readonly ISessionManagerService _sessionManager;
        private readonly IConversationService _conversationService;
        private readonly IWeaviateService? _weaviateService;
        private readonly IConnectionMultiplexer? _redis;
        private readonly HttpClient _httpClient;
        private readonly string _openAiApiKey;
        private readonly string _connectionString;

        // Conversation type triggers (case-insensitive)
        private static readonly Dictionary<string, ConversationType> ConversationTriggers = new()
        {
            // Daily prompt triggers
            { "daily prompt", ConversationType.DAILY_PROMPT },
            { "give me my daily prompt", ConversationType.DAILY_PROMPT },
            { "today's prompt", ConversationType.DAILY_PROMPT },
            { "leadership prompt", ConversationType.DAILY_PROMPT },
            { "talk more about today's prompt", ConversationType.DAILY_PROMPT },
            { "talk more about todays prompt", ConversationType.DAILY_PROMPT },
            { "more about the prompt", ConversationType.DAILY_PROMPT },
            
            // Burning fires triggers
            { "burning fire", ConversationType.BURNING_FIRE },
            { "burning fires", ConversationType.BURNING_FIRE },
            { "urgent help", ConversationType.BURNING_FIRE },
            { "emergency", ConversationType.BURNING_FIRE },
            { "crisis", ConversationType.BURNING_FIRE },
            
            // Other tinker level triggers
            { "other tinker level fires", ConversationType.TINKER_LEVEL },
            { "other tinker level issues", ConversationType.TINKER_LEVEL },
            { "tinker level", ConversationType.TINKER_LEVEL },
            { "tinker issue", ConversationType.TINKER_LEVEL },
            { "tinker problem", ConversationType.TINKER_LEVEL },
            
            // Session management
            { "done for the day", ConversationType.SESSION_END },
            { "done for today", ConversationType.SESSION_END },
            { "i'm done", ConversationType.SESSION_END },
            { "finished", ConversationType.SESSION_END }
        };

        public ChatController(
            ILogger<ChatController> logger,
            ISessionManagerService sessionManager,
            IConversationService conversationService,
            IConfiguration configuration,
            IConnectionMultiplexer? redis = null,
            IWeaviateService? weaviateService = null,
            IHttpClientFactory? httpClientFactory = null)
        {
            _logger = logger;
            _sessionManager = sessionManager;
            _conversationService = conversationService;
            _weaviateService = weaviateService;
            _redis = redis;
            _httpClient = httpClientFactory?.CreateClient() ?? new HttpClient();
            _openAiApiKey = configuration["OpenAI:ApiKey"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
            
            _httpClient.DefaultRequestHeaders.Authorization = 
                new AuthenticationHeaderValue("Bearer", _openAiApiKey);
        }

        [HttpPost("message")]
        public async Task<IActionResult> SendMessage([FromBody] ChatRequest request)
        {
            try
            {
                var userId = User.FindFirst("userId")?.Value ?? "anonymous";
                var firstName = User.FindFirst("firstName")?.Value ?? "User";
                var businessName = User.FindFirst("businessName")?.Value ?? "Your Business";
                
                // Detect conversation type from message
                var conversationType = DetectConversationType(request.Message);
                
                // Get or create appropriate session/thread
                var thread = await _sessionManager.GetActiveThread(userId, (TinkerGenie.API.Services.ConversationType)conversationType);
                
                ChatResponse response;
                
                switch (conversationType)
                {
                    case ConversationType.DAILY_PROMPT:
                        response = await HandleDailyPromptFlow(userId, firstName, businessName, request.Message, thread);
                        break;
                        
                    case ConversationType.BURNING_FIRE:
                        // Create new session for burning fires
                        thread = await _sessionManager.CreateThread(userId, TinkerGenie.API.Services.ConversationType.BURNING_FIRE, "Urgent Issue");
                        response = await HandleBurningFire(userId, request.Message, thread);
                        break;
                        
                    case ConversationType.TINKER_LEVEL:
                        // Create new session for tinker level issues
                        thread = await _sessionManager.CreateThread(userId, TinkerGenie.API.Services.ConversationType.TINKER_LEVEL, "Tinker Level Issue");
                        response = await HandleTinkerLevel(userId, request.Message, thread);
                        break;
                        
                    case ConversationType.SESSION_END:
                        response = await HandleSessionEnd(userId);
                        break;
                        
                    default:
                        response = await HandleGeneralChat(userId, request.Message, thread);
                        break;
                }
                
                // Update thread with messages if we got a valid response
                if (!string.IsNullOrEmpty(response.Response) && thread != null)
                {
                    await _sessionManager.UpdateThreadMessages(thread.ThreadId, request.Message, response.Response);
                }
                
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SendMessage");
                return Ok(new ChatResponse
                {
                    Response = "Let me help you with that. What specific challenge are you facing?",
                    IsError = true
                });
            }
        }

        [HttpPost("session/start")]
        public async Task<IActionResult> StartSession()
        {
            try
            {
                var userId = User.FindFirst("userId")?.Value ?? "anonymous";
                var firstName = User.FindFirst("firstName")?.Value ?? "User";
                var businessName = User.FindFirst("businessName")?.Value ?? "Your Business";
                
                // Get user's current day
                var currentDay = await GetUserCurrentDay(userId);
                
                // Get daily prompt from database
                var dailyPrompt = await GetDailyPromptFromDatabase(currentDay);
                if (dailyPrompt == null)
                {
                    return Ok(new ChatResponse
                    {
                        Response = "I'm having trouble loading today's prompt. Let's talk about your business instead - what's your biggest challenge right now?",
                        IsError = true
                    });
                }
                
                // Create welcome message as first chat bubble
                var welcomeMessage = $"Good morning, {firstName}! Welcome to Day {currentDay} of your leadership journey.\n\n" +
                                   $"Ready to continue your journey at {businessName}?\n\n" +
                                   dailyPrompt.PromptText;
                
                // Create daily prompt thread
                var thread = await _sessionManager.CreateThread(userId, TinkerGenie.API.Services.ConversationType.DAILY_PROMPT, $"Day {currentDay} Reflection");
                
                return Ok(new
                {
                    response = welcomeMessage,
                    conversationId = thread.ThreadId,
                    isDailyPrompt = true,
                    dayNumber = currentDay,
                    promptTitle = dailyPrompt.PromptTitle,
                    sessionType = "daily_prompt"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting session");
                return Ok(new ChatResponse
                {
                    Response = "Welcome back! What would you like to work on today?",
                    IsError = true
                });
            }
        }

        private async Task<ChatResponse> HandleDailyPromptFlow(string userId, string firstName, string businessName, string message, ConversationThread thread)
        {
            try
            {
                // Check if this is initial prompt request or continuation
                var lowerMessage = message.ToLower().Trim();
                var isInitialRequest = lowerMessage.Contains("daily prompt") || lowerMessage.Contains("today's prompt");
                var isContinuation = lowerMessage.Contains("talk more") || lowerMessage.Contains("more about");
                
                if (isInitialRequest && !isContinuation)
                {
                    // Deliver the daily prompt
                    var currentDay = await GetUserCurrentDay(userId);
                    var dailyPrompt = await GetDailyPromptFromDatabase(currentDay);
                    
                    if (dailyPrompt == null)
                    {
                        return new ChatResponse
                        {
                            Response = "Let me find today's prompt for you... Actually, let's start with this: What's weighing on your mind today?",
                            IsError = true
                        };
                    }
                    
                    var promptMessage = $"Good morning, {firstName}! Welcome to Day {currentDay} of your leadership journey.\n\n" +
                                      $"Ready to continue your journey at {businessName}?\n\n" +
                                      dailyPrompt.PromptText;
                    
                    return new ChatResponse
                    {
                        Response = promptMessage,
                        IsDailyPrompt = true,
                        DayNumber = currentDay,
                        ConversationId = thread.ThreadId
                    };
                }
                else
                {
                    // User is responding to the daily prompt - give empathetic acknowledgment
                    var acknowledgment = GenerateEmpathicAcknowledgment(message);
                    
                    // Mark daily prompt as complete after acknowledgment
                    thread.Metadata["promptCompleted"] = true;
                    
                    // Natural way to present options
                    var options = "\n\nHere's how I can help you today:\n" +
                                "• Want to dig deeper into this? Just say \"talk more about today's prompt\"\n" +
                                "• Got something urgent? Tell me \"burning fire\" and I'll help immediately\n" +
                                "• Other challenges? Say \"other tinker level issues\" and we'll tackle them\n" +
                                "• All set? Just let me know you're \"done for the day\"";
                    
                    return new ChatResponse
                    {
                        Response = acknowledgment + options,
                        IsDailyPrompt = true,
                        ConversationId = thread.ThreadId,
                        IsDailyPromptComplete = true
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in daily prompt flow");
                return new ChatResponse
                {
                    Response = "Tell me what's on your mind today.",
                    IsError = true
                };
            }
        }

        private async Task<ChatResponse> HandleBurningFire(string userId, string message, ConversationThread thread)
        {
            try
            {
                // Extract the actual issue from the message
                var issue = ExtractIssueFromMessage(message);
                
                // Chris Cooper style - direct, actionable, urgent
                var prompt = $@"You are Chris Cooper responding to an URGENT business crisis.

The gym owner says: {issue}

Give them:
1. ONE specific action they can take RIGHT NOW (1-2 sentences max)
2. Ask 'Can you do this in the next hour?'

Be direct. No fluff. No numbered lists. Just immediate action.";

                var aiResponse = await GetAIResponse(prompt);
                
                return new ChatResponse
                {
                    Response = aiResponse,
                    ConversationId = thread.ThreadId,
                    SessionType = "burning_fire"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling burning fire");
                return new ChatResponse
                {
                    Response = "Tell me exactly what's happening right now. Be specific - I need details to help you.",
                    IsError = true
                };
            }
        }

        private async Task<ChatResponse> HandleTinkerLevel(string userId, string message, ConversationThread thread)
        {
            try
            {
                var issue = ExtractIssueFromMessage(message);
                
                // Chris Cooper style for non-urgent but important issues
                var prompt = $@"You are Chris Cooper helping a gym owner with a business challenge.

They say: {issue}

Give them:
1. A brief acknowledgment (1 sentence)
2. One clear next step they should take (2 sentences max)
3. Ask what specific outcome they want

Keep it conversational and direct. No lists or formal structure.";

                var aiResponse = await GetAIResponse(prompt);
                
                return new ChatResponse
                {
                    Response = aiResponse,
                    ConversationId = thread.ThreadId,
                    SessionType = "tinker_level"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling tinker level issue");
                return new ChatResponse
                {
                    Response = "What specific challenge are you working through? Give me the details.",
                    IsError = true
                };
            }
        }

        private async Task<ChatResponse> HandleSessionEnd(string userId)
        {
            try
            {
                // Clear the user's session
                await _sessionManager.ClearSession(userId);
                
                // Encouraging sign-off
                var signoff = GenerateSessionEndMessage();
                
                return new ChatResponse
                {
                    Response = signoff,
                    SessionType = "session_end",
                    IsSessionEnd = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ending session");
                return new ChatResponse
                {
                    Response = "Great work today! Rest well - tomorrow brings new opportunities to grow.",
                    IsError = true
                };
            }
        }

        private async Task<ChatResponse> HandleGeneralChat(string userId, string message, ConversationThread thread)
        {
            try
            {
                // Natural conversation with Chris Cooper style
                var prompt = $@"You are Chris Cooper, master business coach for gym owners.

User says: {message}

Respond naturally in 2-3 sentences max. Be direct and helpful.
End with one actionable question that moves them forward.
No formal structure or lists.";

                var aiResponse = await GetAIResponse(prompt);
                
                return new ChatResponse
                {
                    Response = aiResponse,
                    ConversationId = thread.ThreadId
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in general chat");
                return new ChatResponse
                {
                    Response = "I hear you. What's the real challenge behind that?",
                    IsError = true
                };
            }
        }

        private ConversationType DetectConversationType(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return ConversationType.GENERAL_CHAT;
            
            var lowerMessage = message.ToLower().Trim();
            
            // Check each trigger phrase (case-insensitive)
            foreach (var trigger in ConversationTriggers)
            {
                if (lowerMessage.Contains(trigger.Key))
                {
                    return trigger.Value;
                }
            }
            
            return ConversationType.GENERAL_CHAT;
        }

        private string GenerateEmpathicAcknowledgment(string userResponse)
        {
            var lowerResponse = userResponse.ToLower();
            
            // Contextual responses based on what they shared
            if (lowerResponse.Contains("tired") || lowerResponse.Contains("exhausted"))
            {
                return "I hear you - being tired is real, and acknowledging it instead of pushing through is actually strong leadership. Running a business is demanding, and recognizing when you need to recharge is crucial.";
            }
            else if (lowerResponse.Contains("stressed") || lowerResponse.Contains("overwhelmed"))
            {
                return "That stress you're feeling? It's valid. Every successful gym owner faces overwhelming moments. The fact that you're noticing it instead of ignoring it shows real self-awareness.";
            }
            else if (lowerResponse.Contains("angry") || lowerResponse.Contains("frustrated"))
            {
                return "Frustration is information - it's telling you something needs to change. Good leaders feel it, acknowledge it, and then channel it into action.";
            }
            else if (lowerResponse.Contains("good") || lowerResponse.Contains("great") || lowerResponse.Contains("excited"))
            {
                return "That's fantastic! When things are going well, it's important to notice and appreciate it. This positive energy is fuel for your business.";
            }
            else if (lowerResponse.Contains("confused") || lowerResponse.Contains("uncertain"))
            {
                return "Uncertainty is part of the journey. The best gym owners aren't the ones who always know the answer - they're the ones who keep moving forward despite the confusion.";
            }
            else
            {
                return "Thank you for sharing that. Being honest about where you are right now is the foundation of real growth. Every successful gym owner starts exactly where you are.";
            }
        }

        private string ExtractIssueFromMessage(string message)
        {
            // Remove trigger phrases to get the actual issue
            var cleanMessage = message;
            var triggers = new[] { "burning fire", "burning fires", "urgent", "emergency", "other tinker level", "tinker issue" };
            
            foreach (var trigger in triggers)
            {
                cleanMessage = Regex.Replace(cleanMessage, trigger, "", RegexOptions.IgnoreCase).Trim();
            }
            
            // If nothing left after removing triggers, return original
            return string.IsNullOrWhiteSpace(cleanMessage) ? message : cleanMessage;
        }

        private string GenerateSessionEndMessage()
        {
            var messages = new[]
            {
                "Excellent work today! You showed up, did the work, and that's what separates successful gym owners from everyone else. Rest well.",
                "That's a wrap for today! Remember, progress happens in small daily actions. You took yours today. See you tomorrow.",
                "Great session! You're building the habits that create lasting success. Get some rest - tomorrow we go again.",
                "You did the work today, and that matters. Every day you show up, you're getting closer to your goals. Rest up!",
                "Another day of progress in the books! The best gym owners know when to push and when to rest. See you tomorrow."
            };
            
            return messages[new Random().Next(messages.Length)];
        }

        private async Task<string> GetAIResponse(string systemPrompt)
        {
            try
            {
                var request = new
                {
                    model = "gpt-4o-mini",
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = "Provide your response." }
                    },
                    max_tokens = 200, // Keep responses concise
                    temperature = 0.7
                };

                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var responseObj = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    
                    return responseObj.GetProperty("choices")[0]
                                     .GetProperty("message")
                                     .GetProperty("content").GetString() ?? 
                           "Tell me more about what you're facing.";
                }
                
                return "What specific challenge can I help you with?";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling OpenAI API");
                return "Let's focus on what matters most for your business right now.";
            }
        }

        private async Task<int> GetUserCurrentDay(string userId)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                
                var cmd = new NpgsqlCommand(@"
                    SELECT COALESCE(current_day, 1) as current_day
                    FROM user_data
                    WHERE user_id = @userId OR id::text = @userId
                    LIMIT 1", conn);
                    
                cmd.Parameters.AddWithValue("userId", userId.ToLower());
                
                var result = await cmd.ExecuteScalarAsync();
                return result != null ? Convert.ToInt32(result) : 1;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user current day");
                return 1;
            }
        }

        private async Task<DailyPrompt?> GetDailyPromptFromDatabase(int dayNumber)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                
                var cmd = new NpgsqlCommand(@"
                    SELECT prompt_title, prompt_text, day_number
                    FROM leadership_daily_prompts
                    WHERE day_number = @dayNumber
                    LIMIT 1", conn);
                    
                cmd.Parameters.AddWithValue("dayNumber", dayNumber);
                
                await using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new DailyPrompt
                    {
                        PromptTitle = reader.GetString(0),
                        PromptText = reader.GetString(1),
                        DayNumber = reader.GetInt32(2)
                    };
                }
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching daily prompt");
                return null;
            }
        }
    }

    public class ChatRequest
    {
        public string Message { get; set; } = "";
        public string? ConversationId { get; set; }
        public string? SessionType { get; set; }
    }

    public class ChatResponse
    {
        public string Response { get; set; } = "";
        public string? ConversationId { get; set; }
        public bool IsError { get; set; }
        public bool IsDailyPrompt { get; set; }
        public int? DayNumber { get; set; }
        public bool IsDailyPromptComplete { get; set; }
        public string? SessionType { get; set; }
        public bool IsSessionEnd { get; set; }
    }

    public class DailyPrompt
    {
        public string PromptTitle { get; set; } = "";
        public string PromptText { get; set; } = "";
        public int DayNumber { get; set; }
    }

    // ConversationType enum moved to Services namespace to avoid conflicts
}
