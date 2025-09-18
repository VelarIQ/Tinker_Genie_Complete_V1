using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using System.Text.Json;
using TinkerGenie.API.Services;
using StackExchange.Redis;
using Npgsql;
using System.Net.Http.Headers;

namespace TinkerGenie.API.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly ILogger<ChatHub> _logger;
        private readonly IWeaviateService? _weaviateService;
        private readonly IConnectionMultiplexer? _redis;
        private readonly IConversationService _conversationService;
        private readonly HttpClient _httpClient;
        private readonly string _openAiApiKey;
        private readonly string _connectionString;
        private readonly IConfiguration _configuration;

        public ChatHub(
            ILogger<ChatHub> logger,
            IConfiguration configuration,
            IConversationService conversationService,
            IConnectionMultiplexer? redis = null,
            IWeaviateService? weaviateService = null,
            IHttpClientFactory? httpClientFactory = null)
        {
            _logger = logger;
            _configuration = configuration;
            _conversationService = conversationService;
            _weaviateService = weaviateService;
            _redis = redis;
            _httpClient = httpClientFactory?.CreateClient() ?? new HttpClient();
            _openAiApiKey = configuration["OpenAI:ApiKey"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
            
            _httpClient.DefaultRequestHeaders.Authorization = 
                new AuthenticationHeaderValue("Bearer", _openAiApiKey);
        }

        public async Task SendMessage(string message, string? conversationId = null)
        {
            try
            {
                _logger.LogInformation("WebSocket message received: {Message}", message);
                
                // Get user info from SignalR context
                var userId = Context.User?.FindFirst("userId")?.Value ?? "anonymous";
                var firstName = Context.User?.FindFirst("firstName")?.Value ?? "User";
                var businessName = Context.User?.FindFirst("businessName")?.Value ?? "Unknown Business";
                
                // Show typing indicator
                await Clients.Caller.SendAsync("ReceiveTypingIndicator", true);
                
                string aiResponse;
                bool isDailyPrompt = false;
                int? dayNumber = null;
                
                // Check if this is a daily prompt request
                if (IsDailyPromptRequest(message))
                {
                    var promptResult = await HandleDailyPromptRequest(userId, firstName, businessName, conversationId);
                    aiResponse = promptResult.Response;
                    isDailyPrompt = promptResult.IsDailyPrompt;
                    dayNumber = promptResult.DayNumber;
                }
                else if (IsBurningFiresRequest(message))
                {
                    var burningResult = await HandleBurningFiresRequest(userId, message, conversationId);
                    aiResponse = burningResult.Response;
                }
                else
                {
                    // Regular chat - use existing chat logic
                    var regularResult = await HandleRegularChat(userId, message, conversationId);
                    aiResponse = regularResult.Response;
                    conversationId = regularResult.ConversationId;
                }
                
                // Save conversation if needed
                if (!string.IsNullOrEmpty(message) && !string.IsNullOrEmpty(aiResponse))
                {
                    await _conversationService.SaveConversation(userId, message, aiResponse);
                }
                
                // Hide typing indicator
                await Clients.Caller.SendAsync("ReceiveTypingIndicator", false);
                
                // Send response
                await Clients.Caller.SendAsync("ReceiveMessage", new
                {
                    type = "ReceiveMessage",
                    message = aiResponse,
                    conversationId = conversationId,
                    timestamp = DateTime.UtcNow,
                    isError = false,
                    isDailyPrompt = isDailyPrompt,
                    dayNumber = dayNumber
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SendMessage: {ErrorMessage}", ex.Message);
                
                await Clients.Caller.SendAsync("ReceiveTypingIndicator", false);
                
                var errorResponse = new
                {
                    type = "ReceiveMessage",
                    message = "I'm experiencing some technical difficulties, but I'm still here to help with your leadership journey. What would you like to discuss today?",
                    conversationId = conversationId,
                    timestamp = DateTime.UtcNow,
                    isError = true
                };
                
                await Clients.Caller.SendAsync("ReceiveMessage", errorResponse);
            }
        }

        public async Task SendDailyPromptRequest()
        {
            try
            {
                _logger.LogInformation("Daily prompt request received via WebSocket");
                
                var userId = Context.User?.FindFirst("userId")?.Value ?? "anonymous";
                var firstName = Context.User?.FindFirst("firstName")?.Value ?? "User";
                var businessName = Context.User?.FindFirst("businessName")?.Value ?? "Unknown Business";
                
                // Use the same daily prompt logic as HTTP endpoint
                var promptResult = await HandleDailyPromptRequest(userId, firstName, businessName, null);
                
                await Clients.Caller.SendAsync("ReceiveMessage", new
                {
                    type = "ReceiveMessage",
                    message = promptResult.Response,
                    conversationId = promptResult.ConversationId,
                    isError = false,
                    isDailyPrompt = true,
                    dayNumber = promptResult.DayNumber,
                    questionsRemaining = promptResult.QuestionsRemaining,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SendDailyPromptRequest: {ErrorMessage}", ex.Message);
                
                await Clients.Caller.SendAsync("ReceiveMessage", new
                {
                    type = "ReceiveMessage",
                    message = "I couldn't retrieve your daily prompt right now. Please try again.",
                    isError = true,
                    timestamp = DateTime.UtcNow
                });
            }
        }

        // Reuse logic from ChatController
        private bool IsDailyPromptRequest(string message)
        {
            if (message == "START_DAILY_PROMPT") return true;
            
            var lowerMessage = message?.ToLower() ?? "";
            if (lowerMessage == "start fresh" || lowerMessage == "reset session" || lowerMessage == "clear session")
            {
                return true;
            }
            
            var dailyPromptKeywords = new[] { "daily prompt", "give me my daily prompt", "today's prompt", "leadership prompt" };
            return dailyPromptKeywords.Any(keyword => lowerMessage.Contains(keyword));
        }

        private bool IsBurningFiresRequest(string message)
        {
            var lowerMessage = message?.ToLower() ?? "";
            return lowerMessage.Contains("burning fire") || lowerMessage.Contains("urgent help");
        }

        private async Task<ChatResponse> HandleDailyPromptRequest(string userId, string firstName, string businessName, string? conversationId)
        {
            try
            {
                // Check for existing session cleanup
                if (_redis != null)
                {
                    var db = _redis.GetDatabase();
                    var sessionKey = $"chat:session:{userId}";
                    var existingSession = await db.StringGetAsync(sessionKey);
                    
                    if (!string.IsNullOrEmpty(existingSession))
                    {
                        await db.KeyDeleteAsync(sessionKey);
                        _logger.LogInformation("Cleared existing chat session for user {UserId}", userId);
                    }
                }

                // Get user's current day from database
                var currentDay = await GetUserCurrentDay(userId);
                
                // Get daily prompt from database (not hardcoded!)
                var dailyPrompt = await GetDailyPromptFromDatabase(currentDay);
                if (dailyPrompt == null)
                {
                    return new ChatResponse
                    {
                        Response = "I couldn't find today's leadership prompt. Please contact support if this persists.",
                        IsError = true,
                        ConversationId = conversationId ?? Guid.NewGuid().ToString()
                    };
                }

                // Store session in Redis if available
                if (_redis != null)
                {
                    await StoreDailyPromptSession(userId, currentDay, dailyPrompt);
                }

                _logger.LogInformation("Delivering prompt from DB - Day {Day}: {Title}", 
                    currentDay, dailyPrompt.PromptTitle);
                
                // Return EXACT prompt text from database
                return new ChatResponse
                {
                    Response = dailyPrompt.PromptText,
                    IsDailyPrompt = true,
                    DayNumber = currentDay,
                    IsDailyPromptComplete = false,
                    QuestionsRemaining = 2,
                    ConversationId = conversationId ?? Guid.NewGuid().ToString()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling daily prompt request for user {UserId}", userId);
                return new ChatResponse
                {
                    Response = "I'm having trouble accessing your daily prompt. Please try again.",
                    IsError = true,
                    ConversationId = conversationId ?? Guid.NewGuid().ToString()
                };
            }
        }

        private async Task<ChatResponse> HandleBurningFiresRequest(string userId, string message, string? conversationId)
        {
            try
            {
                // Get Weaviate context if available
                string relevantContext = "";
                if (_weaviateService != null)
                {
                    // TODO: Implement SearchCurriculumAsync
                    // var searchResults = await _weaviateService.SearchCurriculumAsync(message, 3);
                    // relevantContext = string.Join("\n", searchResults);
                }

                // Build burning fires prompt with Chris Cooper persona
                var prompt = BuildBurningFiresPrompt(message, relevantContext);
                
                // Get AI response
                var aiResponse = await GetAIResponse(prompt);
                
                return new ChatResponse
                {
                    Response = aiResponse,
                    ConversationId = conversationId ?? Guid.NewGuid().ToString(),
                    IsBurningFires = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling burning fires request");
                return new ChatResponse
                {
                    Response = "I understand this is urgent. Tell me exactly what's happening right now.",
                    IsError = true,
                    ConversationId = conversationId ?? Guid.NewGuid().ToString()
                };
            }
        }

        private async Task<ChatResponse> HandleRegularChat(string userId, string message, string? conversationId)
        {
            try
            {
                // Get session from Redis if exists
                string? sessionContext = "";
                if (_redis != null)
                {
                    var db = _redis.GetDatabase();
                    var sessionKey = $"chat:session:{userId}";
                    var redisValue = await db.StringGetAsync(sessionKey);
                    sessionContext = redisValue.HasValue ? redisValue.ToString() : "";
                }

                // Get Weaviate context
                string relevantContext = "";
                if (_weaviateService != null)
                {
                    // TODO: Implement SearchLeadershipContentAsync
                    // var searchResults = await _weaviateService.SearchLeadershipContentAsync(message, 3);
                    // relevantContext = string.Join("\n", searchResults);
                }

                // Build prompt with Chris Cooper persona
                var prompt = BuildGeneralChatPrompt(message, sessionContext, relevantContext);
                
                // Get AI response
                var aiResponse = await GetAIResponse(prompt);
                
                // Update session in Redis
                if (_redis != null && !string.IsNullOrEmpty(aiResponse))
                {
                    var db = _redis.GetDatabase();
                    var sessionKey = $"chat:session:{userId}";
                    var newSession = $"User: {message}\nAssistant: {aiResponse}";
                    await db.StringSetAsync(sessionKey, newSession, TimeSpan.FromMinutes(30));
                }
                
                return new ChatResponse
                {
                    Response = aiResponse,
                    ConversationId = conversationId ?? Guid.NewGuid().ToString()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in regular chat");
                return new ChatResponse
                {
                    Response = "Let me help you with that. What specific challenge are you facing?",
                    IsError = true,
                    ConversationId = conversationId ?? Guid.NewGuid().ToString()
                };
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
                _logger.LogError(ex, "Error getting user current day for {UserId}", userId);
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
                _logger.LogError(ex, "Error fetching daily prompt for day {DayNumber}", dayNumber);
                return null;
            }
        }

        private async Task StoreDailyPromptSession(string userId, int currentDay, DailyPrompt prompt)
        {
            if (_redis == null) return;
            
            try
            {
                var db = _redis.GetDatabase();
                var sessionKey = $"chat:session:{userId}";
                
                var sessionData = JsonSerializer.Serialize(new
                {
                    type = "daily_prompt",
                    userId = userId,
                    currentDay = currentDay,
                    promptTitle = prompt.PromptTitle,
                    promptDelivered = prompt.PromptText,
                    questionsAsked = 0,
                    questionsRemaining = 2,
                    timestamp = DateTime.UtcNow
                });
                
                await db.StringSetAsync(sessionKey, sessionData, TimeSpan.FromMinutes(30));
                _logger.LogInformation("Stored daily prompt session for user {UserId}, day {Day}", userId, currentDay);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error storing daily prompt session in Redis");
            }
        }

        private string BuildGeneralChatPrompt(string userMessage, string? sessionContext, string relevantContext)
        {
            var prompt = new System.Text.StringBuilder();
            
            prompt.AppendLine("You are Chris Cooper, master business coach and founder of Two-Brain Business.");
            prompt.AppendLine("You help gym owners build profitable businesses through direct, actionable advice.");
            prompt.AppendLine();
            prompt.AppendLine("Communication style:");
            prompt.AppendLine("- Be direct and concise (2-3 sentences max)");
            prompt.AppendLine("- Speak naturally, like a real human conversation");
            prompt.AppendLine("- Ask one powerful question that drives action");
            prompt.AppendLine("- Focus on measurable results");
            prompt.AppendLine();
            
            if (!string.IsNullOrEmpty(sessionContext))
            {
                prompt.AppendLine("Previous conversation context:");
                prompt.AppendLine(sessionContext);
                prompt.AppendLine();
            }
            
            if (!string.IsNullOrEmpty(relevantContext))
            {
                prompt.AppendLine("Relevant business context:");
                prompt.AppendLine(relevantContext);
                prompt.AppendLine();
            }
            
            prompt.AppendLine($"User message: {userMessage}");
            
            return prompt.ToString();
        }

        private string BuildBurningFiresPrompt(string userMessage, string relevantContext)
        {
            var prompt = new System.Text.StringBuilder();
            
            prompt.AppendLine("You are Chris Cooper responding to an URGENT business crisis.");
            prompt.AppendLine("This is a 'burning fire' that needs immediate action.");
            prompt.AppendLine();
            prompt.AppendLine("Response requirements:");
            prompt.AppendLine("- Give the FIRST step they must take RIGHT NOW");
            prompt.AppendLine("- Be ultra-specific and actionable");
            prompt.AppendLine("- Maximum 2 sentences for the action");
            prompt.AppendLine("- Then ask: 'Can you do this in the next hour?'");
            prompt.AppendLine();
            
            if (!string.IsNullOrEmpty(relevantContext))
            {
                prompt.AppendLine("Business context:");
                prompt.AppendLine(relevantContext);
                prompt.AppendLine();
            }
            
            prompt.AppendLine($"URGENT ISSUE: {userMessage}");
            
            return prompt.ToString();
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
                        new { role = "user", content = "Please provide your coaching response." }
                    },
                    max_tokens = 500,
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
                           "What specific challenge would you like to work on today?";
                }
                
                _logger.LogError("OpenAI API request failed: {StatusCode}", response.StatusCode);
                return "Let's focus on what matters most for your business right now. What's your biggest challenge?";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling OpenAI API");
                return "Tell me what's happening in your business right now.";
            }
        }

        public override async Task OnConnectedAsync()
        {
            var userId = Context.User?.FindFirst("userId")?.Value ?? "unknown";
            _logger.LogInformation("Client connected via WebSocket: {ConnectionId} for user {UserId}", 
                Context.ConnectionId, userId);
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Context.User?.FindFirst("userId")?.Value ?? "unknown";
            _logger.LogInformation("Client disconnected from WebSocket: {ConnectionId} for user {UserId}", 
                Context.ConnectionId, userId);
            await base.OnDisconnectedAsync(exception);
        }

        public Task Ping() => Task.CompletedTask;
    }

    // Helper classes to match ChatController
    public class ChatResponse
    {
        public string Response { get; set; } = "";
        public string? ConversationId { get; set; }
        public bool IsError { get; set; }
        public bool IsDailyPrompt { get; set; }
        public bool IsBurningFires { get; set; }
        public int? DayNumber { get; set; }
        public bool IsDailyPromptComplete { get; set; }
        public int? QuestionsRemaining { get; set; }
    }

    public class DailyPrompt
    {
        public string PromptTitle { get; set; } = "";
        public string PromptText { get; set; } = "";
        public int DayNumber { get; set; }
    }
}