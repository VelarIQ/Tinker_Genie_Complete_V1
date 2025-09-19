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
        private readonly IConnectionMonitorService? _connectionMonitor;
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
            IHttpClientFactory? httpClientFactory = null,
            IConnectionMonitorService? connectionMonitor = null)
        {
            _logger = logger;
            _configuration = configuration;
            _conversationService = conversationService;
            _weaviateService = weaviateService;
            _redis = redis;
            _connectionMonitor = connectionMonitor;
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
                await Clients.Caller.SendAsync("ReceiveTypingIndicator", new 
                {
                    isTyping = true,
                    message = "TinkerGenie is typing..."
                });
                
                string aiResponse;
                bool isDailyPrompt = false;
                int? dayNumber = null;
                
                // Determine conversation type and handle accordingly
                ChatResponse chatResult;
                
                if (IsDailyPromptRequest(message))
                {
                    chatResult = await HandleDailyPromptRequest(userId, firstName, businessName, conversationId);
                }
                else if (IsDoneForDayRequest(message))
                {
                    chatResult = await HandleDoneForDay(userId);
                }
                else if (IsBurningFiresRequest(message))
                {
                    // Create NEW thread for burning fire
                    chatResult = await HandleBurningFiresRequest(userId, message, null);
                }
                else if (IsTinkerLevelRequest(message))
                {
                    // Create NEW thread for tinker level
                    chatResult = await HandleTinkerLevelRequest(userId, message, null);
                }
                else if (IsTalkMoreRequest(message))
                {
                    // Continue in SAME daily prompt thread
                    chatResult = await HandleDailyPromptContinuation(userId, message, conversationId);
                }
                else if (await IsRespondingToDailyPrompt(userId, message))
                {
                    // First response to daily prompt - provide acknowledgment and options
                    chatResult = await HandleFirstDailyPromptResponse(userId, message, conversationId);
                }
                else
                {
                    // Regular chat
                    chatResult = await HandleRegularChat(userId, message, conversationId);
                }
                
                aiResponse = chatResult.Response;
                conversationId = chatResult.ConversationId;
                isDailyPrompt = chatResult.IsDailyPrompt;
                dayNumber = chatResult.DayNumber;
                
                // Save conversation if needed
                if (!string.IsNullOrEmpty(message) && !string.IsNullOrEmpty(aiResponse))
                {
                    await _conversationService.SaveConversation(userId, message, aiResponse);
                }
                
                // Hide typing indicator
                await Clients.Caller.SendAsync("ReceiveTypingIndicator", new 
                {
                    isTyping = false,
                    message = ""
                });
                
                // Check if this is the first response to daily prompt (need to show options)
                bool showOptions = false;
                List<ConversationOption>? options = null;
                
                if (chatResult.ShowOptions)
                {
                    showOptions = true;
                    options = GetConversationOptions();
                    
                    // Append natural options to response
                    var optionsText = new System.Text.StringBuilder();
                    optionsText.AppendLine();
                    optionsText.AppendLine();
                    optionsText.AppendLine("What would help most right now?");
                    optionsText.AppendLine("• Say **'talk more about today's prompt'** to dig deeper into this reflection");
                    optionsText.AppendLine("• Say **'burning fire'** if you have an urgent issue that needs immediate action");
                    optionsText.AppendLine("• Say **'other tinker level issues'** for strategic challenges you're facing");
                    optionsText.AppendLine("• Say **'done for the day'** when you're ready to wrap up");
                    
                    aiResponse += optionsText.ToString();
                }
                
                // Send response
                await Clients.Caller.SendAsync("ReceiveMessage", new
                {
                    type = "ReceiveMessage",
                    message = aiResponse,
                    conversationId = conversationId,
                    timestamp = DateTime.UtcNow,
                    isError = false,
                    isDailyPrompt = isDailyPrompt,
                    dayNumber = dayNumber,
                    isNewThread = chatResult.IsNewThread,
                    threadType = chatResult.ThreadType,
                    showOptions = showOptions,
                    options = options
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SendMessage: {ErrorMessage}", ex.Message);
                
                await Clients.Caller.SendAsync("ReceiveTypingIndicator", new 
                {
                    isTyping = false,
                    message = ""
                });
                
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
            
            var lowerMessage = message?.ToLower().Trim() ?? "";
            if (lowerMessage == "start fresh" || lowerMessage == "reset session" || lowerMessage == "clear session")
            {
                return true;
            }
            
            var dailyPromptKeywords = new[] { "daily prompt", "give me my daily prompt", "today's prompt", "leadership prompt" };
            return dailyPromptKeywords.Any(keyword => lowerMessage.Contains(keyword));
        }

        private bool IsBurningFiresRequest(string message)
        {
            var lowerMessage = message?.ToLower().Trim() ?? "";
            // More flexible matching for urgent issues
            return System.Text.RegularExpressions.Regex.IsMatch(lowerMessage, @"\b(burning\s*fire|urgent\s*help|emergency|crisis)\b");
        }
        
        private bool IsTinkerLevelRequest(string message)
        {
            var lowerMessage = message?.ToLower().Trim() ?? "";
            return System.Text.RegularExpressions.Regex.IsMatch(lowerMessage, @"\b(other\s*tinker|tinker\s*level|tinker\s*fire)\b");
        }
        
        private bool IsTalkMoreRequest(string message)
        {
            var lowerMessage = message?.ToLower().Trim() ?? "";
            return System.Text.RegularExpressions.Regex.IsMatch(lowerMessage, @"\b(talk\s*more|continue\s*prompt|more\s*about\s*today|today'?s?\s*prompt)\b");
        }
        
        private bool IsDoneForDayRequest(string message)
        {
            var lowerMessage = message?.ToLower().Trim() ?? "";
            return System.Text.RegularExpressions.Regex.IsMatch(lowerMessage, @"\b(done\s*for\s*the\s*day|finished|complete|signing\s*off)\b");
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
                
                // Format the prompt naturally
                var response = new System.Text.StringBuilder();
                response.AppendLine($"Good morning! Welcome to Day {currentDay} of your leadership journey.");
                response.AppendLine();
                response.AppendLine(dailyPrompt.PromptText);
                
                return new ChatResponse
                {
                    Response = response.ToString(),
                    IsDailyPrompt = true,
                    DayNumber = currentDay,
                    IsDailyPromptComplete = false,
                    QuestionsRemaining = 2,
                    ConversationId = conversationId ?? Guid.NewGuid().ToString(),
                    IsNewThread = true,
                    ThreadType = "daily_prompt",
                    ShowOptions = false  // Don't show options until they respond
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

        private async Task<ChatResponse> HandleTinkerLevelRequest(string userId, string message, string? conversationId)
        {
            try
            {
                var prompt = new System.Text.StringBuilder();
                prompt.AppendLine("You are Chris Cooper helping a gym owner with a strategic business challenge.");
                prompt.AppendLine();
                prompt.AppendLine($"Their challenge: {message}");
                prompt.AppendLine();
                prompt.AppendLine("Response format:");
                prompt.AppendLine("1. Acknowledge the challenge (1 sentence)");
                prompt.AppendLine("2. Give them the FIRST step to take this week (2 sentences max)");
                prompt.AppendLine("3. Ask what resources they have available");
                prompt.AppendLine();
                prompt.AppendLine("Be specific and actionable. No theory, just practical steps.");
                
                var aiResponse = await GetAIResponse(prompt.ToString());
                
                return new ChatResponse
                {
                    Response = aiResponse,
                    ConversationId = Guid.NewGuid().ToString(), // Always create a new conversation
                    IsNewThread = true,
                    ThreadType = "tinker_level"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling tinker level request");
                return new ChatResponse
                {
                    Response = "Let's work through this challenge together. What specific issue are you facing?",
                    IsError = true,
                    ConversationId = conversationId ?? Guid.NewGuid().ToString()
                };
            }
        }
        
        private async Task<ChatResponse> HandleDailyPromptContinuation(string userId, string message, string? conversationId)
        {
            try
            {
                // Get session context from Redis
                string? sessionContext = "";
                if (_redis != null && !string.IsNullOrEmpty(conversationId))
                {
                    var db = _redis.GetDatabase();
                    var sessionKey = $"chat:session:{userId}";
                    var redisValue = await db.StringGetAsync(sessionKey);
                    sessionContext = redisValue.HasValue ? redisValue.ToString() : "";
                }
                
                var prompt = new System.Text.StringBuilder();
                prompt.AppendLine("You are Chris Cooper, helping a gym owner explore their daily leadership reflection.");
                prompt.AppendLine("They've already done the daily prompt and want to talk more about it.");
                prompt.AppendLine();
                
                if (!string.IsNullOrEmpty(sessionContext))
                {
                    prompt.AppendLine("Previous context:");
                    prompt.AppendLine(sessionContext);
                    prompt.AppendLine();
                }
                
                prompt.AppendLine($"User: {message}");
                prompt.AppendLine();
                prompt.AppendLine("Respond in 2-3 sentences max. Be direct, supportive, and ask one question that helps them go deeper.");
                
                var aiResponse = await GetAIResponse(prompt.ToString());
                
                // Update session
                if (_redis != null)
                {
                    var db = _redis.GetDatabase();
                    var sessionKey = $"chat:session:{userId}";
                    var newSession = $"User: {message}\nAssistant: {aiResponse}";
                    await db.StringSetAsync(sessionKey, newSession, TimeSpan.FromMinutes(30));
                }
                
                return new ChatResponse
                {
                    Response = aiResponse,
                    ConversationId = conversationId ?? Guid.NewGuid().ToString(),
                    ThreadType = "daily_prompt"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling daily prompt continuation");
                return new ChatResponse
                {
                    Response = "Tell me more about what came up for you during today's reflection.",
                    IsError = true,
                    ConversationId = conversationId ?? Guid.NewGuid().ToString()
                };
            }
        }
        
        private async Task<bool> IsRespondingToDailyPrompt(string userId, string message)
        {
            // Check if there's an active daily prompt session awaiting response
            if (_redis == null) return false;
            
            try
            {
                var db = _redis.GetDatabase();
                var sessionData = await db.StringGetAsync($"session:{userId}:daily");
                if (!string.IsNullOrEmpty(sessionData))
                {
                    var data = JsonSerializer.Deserialize<JsonElement>(sessionData!);
                    return data.TryGetProperty("awaitingResponse", out var awaiting) && awaiting.GetBoolean();
                }
            }
            catch { }
            
            return false;
        }
        
        private async Task<ChatResponse> HandleFirstDailyPromptResponse(string userId, string userResponse, string? conversationId)
        {
            try
            {
                // Clear the awaiting flag
                if (_redis != null)
                {
                    var db = _redis.GetDatabase();
                    await db.KeyDeleteAsync($"session:{userId}:daily");
                }
                
                // Get acknowledgment based on their response
                var acknowledgment = GetPersonalizedAcknowledgment(userResponse);
                
                // Save this exchange
                await _conversationService.SaveConversation(userId, userResponse, acknowledgment);
                
                return new ChatResponse
                {
                    Response = acknowledgment,
                    ConversationId = conversationId ?? Guid.NewGuid().ToString(),
                    ThreadType = "daily_prompt",
                    ShowOptions = true  // Show options after acknowledgment
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling first daily prompt response");
                return new ChatResponse
                {
                    Response = "I hear you. What would you like to explore about this?",
                    ConversationId = conversationId ?? Guid.NewGuid().ToString(),
                    ShowOptions = true
                };
            }
        }
        
        private string GetPersonalizedAcknowledgment(string userResponse)
        {
            var responses = new[]
            {
                "I hear you - {feeling} is real, and acknowledging it takes courage. You're doing the hard work of honest leadership.",
                "That's powerful self-awareness. {feeling} tells us something important about what you need right now.",
                "Thank you for being honest. {feeling} is information, not weakness. This is exactly what great leaders do.",
                "I get it. {feeling} is part of the journey. The fact that you're here, noticing it - that's leadership."
            };

            // Extract the feeling/state from response
            var feeling = ExtractFeeling(userResponse);
            var template = responses[new Random().Next(responses.Length)];
            return template.Replace("{feeling}", feeling);
        }
        
        private string ExtractFeeling(string response)
        {
            var lower = response.ToLower();
            
            // Common feelings/states
            if (lower.Contains("tired")) return "Being tired";
            if (lower.Contains("overwhelmed")) return "Feeling overwhelmed";
            if (lower.Contains("stressed")) return "Stress";
            if (lower.Contains("anxious")) return "Anxiety";
            if (lower.Contains("frustrated")) return "Frustration";
            if (lower.Contains("excited")) return "Excitement";
            if (lower.Contains("confident")) return "Confidence";
            if (lower.Contains("uncertain")) return "Uncertainty";
            if (lower.Contains("good")) return "Feeling good";
            if (lower.Contains("great")) return "Feeling great";
            
            // Default
            return "What you're feeling";
        }
        
        private async Task<ChatResponse> HandleDoneForDay(string userId)
        {
            try
            {
                // Mark daily prompt as complete in database
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                
                var cmd = new NpgsqlCommand(@"
                    UPDATE user_profiles 
                    SET current_day = current_day + 1,
                        last_prompt_completed = NOW()
                    WHERE user_id = @userId", conn);
                    
                cmd.Parameters.AddWithValue("userId", userId);
                await cmd.ExecuteNonQueryAsync();
                
                // Clear session
                if (_redis != null)
                {
                    var db = _redis.GetDatabase();
                    await db.KeyDeleteAsync($"chat:session:{userId}");
                }
                
                return new ChatResponse
                {
                    Response = "Great work today! You showed up, and that's what matters. Rest up - tomorrow's prompt will be waiting when you're ready. See you then, champion.",
                    IsDailyPromptComplete = true,
                    ThreadType = "completion"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling done for day");
                return new ChatResponse
                {
                    Response = "You did great work today. See you tomorrow for your next leadership prompt!",
                    IsDailyPromptComplete = true
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
                    ConversationId = Guid.NewGuid().ToString(), // Always create a new conversation
                    IsNewThread = true,
                    ThreadType = "burning_fires",
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
            
            prompt.AppendLine("You are Chris Cooper responding to a gym owner's URGENT crisis.");
            prompt.AppendLine();
            prompt.AppendLine($"Their urgent issue: {userMessage}");
            prompt.AppendLine();
            prompt.AppendLine("Give them ONE specific action they can take RIGHT NOW (within the next hour).");
            prompt.AppendLine("Be ultra-direct. 2 sentences max for the action.");
            prompt.AppendLine("End with: 'Can you do this in the next hour?'");
            prompt.AppendLine();
            prompt.AppendLine("Focus on immediate damage control, not long-term strategy.");
            prompt.AppendLine("Do NOT use numbered lists or bullet points. Just direct action.");
            
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
            var userId = Context.User?.FindFirst("userId")?.Value ?? 
                        Context.User?.FindFirst("sub")?.Value ?? "unknown";
            
            _logger.LogInformation("Client connected via WebSocket: {ConnectionId} for user {UserId}", 
                Context.ConnectionId, userId);
            
            // Track connection for monitoring
            _connectionMonitor?.OnConnected(Context.ConnectionId, userId);
            
            // Add to user group for targeted messages
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{userId}");
            
            // Get connection stats
            if (_connectionMonitor != null)
            {
                var stats = _connectionMonitor.GetConnectionStats();
                if (stats["capacity_percentage"] > 90)
                {
                    _logger.LogWarning("Server at {Percentage}% capacity", stats["capacity_percentage"]);
                }
            }
            
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Context.User?.FindFirst("userId")?.Value ?? 
                        Context.User?.FindFirst("sub")?.Value ?? "unknown";
            
            _logger.LogInformation("Client disconnected from WebSocket: {ConnectionId} for user {UserId}", 
                Context.ConnectionId, userId);
            
            // Remove from tracking
            _connectionMonitor?.OnDisconnected(Context.ConnectionId);
            
            // Remove from user group
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user-{userId}");
            
            if (exception != null)
            {
                _logger.LogError(exception, "WebSocket disconnected with error");
            }
            
            await base.OnDisconnectedAsync(exception);
        }

        public Task Ping() => Task.CompletedTask;
        
        private List<ConversationOption> GetConversationOptions()
        {
            return new List<ConversationOption>
            {
                new() { 
                    Phrase = "talk more about today's prompt",
                    Description = "Dig deeper into this reflection"
                },
                new() { 
                    Phrase = "burning fire",
                    Description = "Urgent issue needing immediate action"
                },
                new() { 
                    Phrase = "other tinker level issues",
                    Description = "Strategic challenges you're facing"
                },
                new() { 
                    Phrase = "done for the day",
                    Description = "Wrap up today's session"
                }
            };
        }
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
        public bool IsNewThread { get; set; }
        public string? ThreadType { get; set; }
        public bool ShowOptions { get; set; }
        public List<ConversationOption>? Options { get; set; }
    }
    
    public class ConversationOption
    {
        public string Phrase { get; set; } = "";
        public string Description { get; set; } = "";
    }

    public class DailyPrompt
    {
        public string PromptTitle { get; set; } = "";
        public string PromptText { get; set; } = "";
        public int DayNumber { get; set; }
    }
}