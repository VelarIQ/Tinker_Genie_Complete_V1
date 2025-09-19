using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Text;
using Npgsql;
using TinkerGenie.API.Services;
using StackExchange.Redis;
using System.Security.Claims;
using System.Text.Json;

namespace TinkerGenie.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ChatController : ControllerBase
    {
        private readonly ILogger<ChatController> _logger;
        private readonly IConversationService _conversationService;
        private readonly IWeaviateService? _weaviateService;
        private readonly ICurriculumSearchService? _curriculumSearchService;
        private readonly IConnectionMultiplexer? _redis;
        private readonly HttpClient _httpClient;
        private readonly string _openAiApiKey;
        private readonly string _connectionString;

        public ChatController(
            IConfiguration configuration, 
            ILogger<ChatController> logger, 
            IConversationService conversationService,
            IConnectionMultiplexer? redis = null,
            IWeaviateService? weaviateService = null,
            ICurriculumSearchService? curriculumSearchService = null)
        {
            _logger = logger;
            _conversationService = conversationService;
            _weaviateService = weaviateService;
            _curriculumSearchService = curriculumSearchService;
            _redis = redis;
            _httpClient = new HttpClient();
            _openAiApiKey = configuration["OpenAI:ApiKey"] ?? "";
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
        }

        // USER REQUESTED: New Chat endpoint
        [HttpPost("new")]
        public async Task<IActionResult> NewChat()
        {
            try
            {
                var userId = User.FindFirst("userId")?.Value ?? "";
                var name = User.FindFirst("name")?.Value ?? "Leader";
                
                await ClearAllActiveSessions(userId);
                var newConversationId = Guid.NewGuid().ToString();
                var timeOfDay = GetTimeOfDay();
                var welcomeMessage = GetWelcomeMessage(timeOfDay, name);
                
                return Ok(new
                {
                    success = true,
                    conversationId = newConversationId,
                    message = welcomeMessage,
                    sessionCleared = true,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating new chat");
                return Ok(new { success = false, error = "Failed to create new chat session" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Chat([FromBody] ChatRequest request)
        {
            try
            {
                var userId = User.FindFirst("userId")?.Value ?? "";
                if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(request.Message))
                {
                    return BadRequest(new { success = false, message = "Invalid request" });
                }

                var firstName = User.FindFirst("name")?.Value ?? "Leader";
                
                // Check if this is a daily prompt request
                if (IsDailyPromptRequest(request.Message))
                {
                    return await HandleDailyPromptRequest(userId, firstName, request.ConversationId, request.Message);
                }

                // Check if user is in a daily prompt session
                var dailyPromptSession = await GetDailyPromptSession(userId);
                if (dailyPromptSession != null)
                {
                    return await HandleDailyPromptResponse(userId, request.Message, dailyPromptSession, request.ConversationId);
                }

                // USER'S SIMPLE FLOW: Check if user is in a burning fires session
                var burningFiresSession = await GetBurningFiresSession(userId);
                if (burningFiresSession != null)
                {
                    return await HandleBurningFiresFollowUp(userId, request.Message, burningFiresSession);
                }

                // Check for burning fires scenario (initial trigger)
                if (IsBurningFiresScenario(request.Message))
                {
                    return await HandleBurningFiresRequest(userId, request.Message, request.ConversationId);
                }

                // Regular chat
                return await HandleRegularChat(userId, request.Message, request.ConversationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in chat endpoint");
                return Ok(new ChatResponse { Response = "I encountered an error. Please try again.", IsError = true });
            }
        }

        private bool IsDailyPromptRequest(string message)
        {
            if (message == "START_DAILY_PROMPT") return true;
            var lowerMessage = message?.ToLower() ?? "";
            if (lowerMessage == "start fresh" || lowerMessage == "reset session") return true;
            var keywords = new[] { "daily prompt", "today's prompt", "leadership prompt" };
            return keywords.Any(keyword => lowerMessage.Contains(keyword));
        }

        // USER'S SIMPLE FLOW: Burning fires detection
        private bool IsBurningFiresScenario(string message)
        {
            var urgentPatterns = new[] {
                "burning fire", "burning fires", "urgent", "emergency", "crisis",
                "immediate help", "need help now", "urgent situation"
            };
            
            var lowerMessage = message?.ToLower() ?? "";
            return urgentPatterns.Any(pattern => lowerMessage.Contains(pattern));
        }

        // USER'S SIMPLE FLOW: Phase 1 - New session + discovery questions
        private async Task<IActionResult> HandleBurningFiresRequest(string userId, string userMessage, string? conversationId)
        {
            try
            {
                // 1. NEW SESSION (separate sidebar entry)
                var newConversationId = Guid.NewGuid().ToString();
                _logger.LogInformation("Creating new burning fires session for user {UserId}", userId);
                
                // 2. 2-3 DISCOVERY QUESTIONS (acknowledge + empathize)
                var discoveryResponse = "I understand you have some urgent issues that need attention. I'm here to help you find the best solutions from our knowledge base.\n\nWhat specifically is the burning fire you're dealing with? Is it a cash flow issue, staff problem, customer situation, or something operational?";
                
                // Store burning fires session for Phase 2
                await StoreBurningFiresSession(userId, newConversationId, userMessage);
                
                // Save discovery conversation (creates new sidebar entry)
                await _conversationService.SaveConversation(userId, userMessage, discoveryResponse, newConversationId);
                
                return Ok(new ChatResponse
                {
                    Response = discoveryResponse,
                    IsBurningFires = true,
                    ConversationId = newConversationId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling burning fires request");
                return Ok(new ChatResponse { Response = "I understand you have urgent issues. Can you describe what's happening?", IsBurningFires = true });
            }
        }

        // USER'S SIMPLE FLOW: Phase 2 - Knowledge base search + clickable links
        private async Task<IActionResult> HandleBurningFiresFollowUp(string userId, string userMessage, BurningFiresSession session)
        {
            try
            {
                // 3. SEMANTIC SEARCH WEAVIATE CURRICULUM (using YOUR existing method)
                // 4. SEARCH TITLES IN REDIS 
                // 5. SEARCH PARAGRAPHS UNDER TITLES
                if (_curriculumSearchService != null)
                {
                    var searchResult = await _curriculumSearchService.SearchForBurningFires(userMessage);
                    
                    // 6. PARSE CLICKABLE LINKS + 7. NEW BROWSER WINDOW + 8. SHORT REASONING
                    var solutionWithLinks = FormatBurningFiresSolution(userMessage, searchResult);
                    
                    // Save solution conversation (same session)
                    await _conversationService.SaveConversation(userId, userMessage, solutionWithLinks, session.ConversationId);
                    
                    // Clear session after providing solution
                    await ClearBurningFiresSession(userId);
                    
                    return Ok(new ChatResponse
                    {
                        Response = solutionWithLinks,
                        IsBurningFires = true,
                        ConversationId = session.ConversationId
                    });
                }
                else
                {
                    return Ok(new ChatResponse
                    {
                        Response = "I understand this is urgent. Let me help you find the right resources for this situation.",
                        IsBurningFires = true,
                        ConversationId = session.ConversationId
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in burning fires follow-up");
                return Ok(new ChatResponse
                {
                    Response = "Can you tell me more details about the specific issue?",
                    IsBurningFires = true,
                    ConversationId = session.ConversationId
                });
            }
        }

        // USER'S SIMPLE FLOW: Format with clickable links + reasoning
        private string FormatBurningFiresSolution(string userIssue, CurriculumSearchResponse searchResult)
        {
            var solution = new StringBuilder();
            
            solution.AppendLine($"Based on your '{userIssue}' situation, I found these specific solutions from our knowledge base:");
            solution.AppendLine();
            
            if (searchResult.Resources != null && searchResult.Resources.Any())
            {
                foreach (var resource in searchResult.Resources.Take(3))
                {
                    solution.AppendLine($"📋 **{resource.Title}**");
                    solution.AppendLine($"💡 {resource.Preview}");
                    solution.AppendLine($"🔗 https://twobrain.com{resource.Url}");
                    solution.AppendLine($"✨ *Selected because: This directly addresses {userIssue} challenges and has helped other business owners in similar situations*");
                    solution.AppendLine();
                }
            }
            else
            {
                solution.AppendLine("🔗 https://twobrain.com/crisis-management");
                solution.AppendLine("✨ *Selected because: Comprehensive crisis management framework for urgent business situations*");
            }
            
            solution.AppendLine("Click any link above to open the full resource in a new browser window. Which approach makes the most sense for your situation?");
            
            return solution.ToString();
        }

        // Session management methods
        private async Task<BurningFiresSession?> GetBurningFiresSession(string userId)
        {
            try
            {
                if (_redis?.IsConnected == true)
                {
                    var db = _redis.GetDatabase();
                    var sessionJson = await db.StringGetAsync($"burning_fires_session:{userId}");
                    
                    if (sessionJson.HasValue && !string.IsNullOrEmpty(sessionJson))
                    {
                        return JsonSerializer.Deserialize<BurningFiresSession>(sessionJson!);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving burning fires session");
            }
            return null;
        }

        private async Task StoreBurningFiresSession(string userId, string conversationId, string initialIssue)
        {
            try
            {
                if (_redis?.IsConnected == true)
                {
                    var session = new BurningFiresSession
                    {
                        UserId = userId,
                        ConversationId = conversationId,
                        InitialIssue = initialIssue,
                        CreatedAt = DateTime.UtcNow,
                        ConversationHistory = new List<string>()
                    };
                    
                    var sessionJson = JsonSerializer.Serialize(session);
                    var db = _redis.GetDatabase();
                    await db.StringSetAsync($"burning_fires_session:{userId}", sessionJson, TimeSpan.FromHours(24));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error storing burning fires session");
            }
        }

        private async Task ClearBurningFiresSession(string userId)
        {
            try
            {
                if (_redis?.IsConnected == true)
                {
                    var db = _redis.GetDatabase();
                    await db.KeyDeleteAsync($"burning_fires_session:{userId}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing burning fires session");
            }
        }

        // All other working methods from baseline...
        private async Task<IActionResult> HandleDailyPromptRequest(string userId, string firstName, string? conversationId, string? originalMessage)
        {
            try
            {
                var isStartFresh = originalMessage?.ToLower().Contains("start fresh") == true;
                var existingSession = await GetDailyPromptSession(userId);
                
                if (existingSession != null && !isStartFresh)
                {
                    return Ok(new ChatResponse
                    {
                        Response = $"You have an active Daily Prompt session for Day {existingSession.DayNumber}. Type 'start fresh' to reset and begin a new prompt, or continue with your current reflection.",
                        IsDailyPrompt = true,
                        DayNumber = existingSession.DayNumber,
                        ConversationId = conversationId ?? Guid.NewGuid().ToString()
                    });
                }
                
                if (existingSession != null && isStartFresh)
                {
                    await ClearDailyPromptSession(userId);
                }

                var currentDay = await GetUserCurrentDay(userId);
                var dailyPrompt = await GetDailyPromptFromDatabase(currentDay);
                if (dailyPrompt == null)
                {
                    return Ok(new ChatResponse { Response = "No daily prompt available.", IsError = true });
                }

                await StoreDailyPromptSession(userId, currentDay, dailyPrompt);
                var finalConversationId = conversationId ?? Guid.NewGuid().ToString();
                await _conversationService.SaveConversation(userId, "START_DAILY_PROMPT", dailyPrompt.PromptText, finalConversationId);

                return Ok(new ChatResponse
                {
                    Response = dailyPrompt.PromptText,
                    IsDailyPrompt = true,
                    DayNumber = currentDay,
                    ConversationId = finalConversationId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in daily prompt request");
                return Ok(new ChatResponse { Response = "Error getting daily prompt.", IsError = true });
            }
        }

        private async Task<IActionResult> HandleDailyPromptResponse(string userId, string userMessage, DailyPromptSession session, string? conversationId)
        {
            try
            {
                var lowerMessage = userMessage.ToLower();
                
                if (lowerMessage.Contains("done for the day"))
                {
                    await CompleteDailyPrompt(userId, session.DayNumber);
                    var userStyle = await GetUserCommunicationStyle(userId);
                    var completionMessage = GetCompletionMessage(session.DayNumber, userStyle);
                    await _conversationService.SaveConversation(userId, userMessage, completionMessage, conversationId);
                    
                    return Ok(new ChatResponse
                    {
                        Response = completionMessage,
                        IsDailyPrompt = true,
                        DayNumber = session.DayNumber,
                        IsDailyPromptComplete = true,
                        ConversationId = conversationId ?? Guid.NewGuid().ToString()
                    });
                }
                
                var aiPrompt = await BuildDailyPromptAIPrompt(userMessage, session, userId);
                var aiResponse = await GetAIResponse(aiPrompt);
                var acknowledgment = GetAcknowledgmentResponse(aiResponse);
                var followUp = await GetFollowUpWithKeyPhrases(session);
                var fullResponse = acknowledgment + "|SPLIT|" + followUp;
                
                await _conversationService.SaveConversation(userId, userMessage, fullResponse, conversationId);
                await UpdateDailyPromptSession(userId, session);

                return Ok(new ChatResponse
                {
                    Response = fullResponse,
                    IsDailyPrompt = true,
                    DayNumber = session.DayNumber,
                    ConversationId = conversationId ?? Guid.NewGuid().ToString()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in daily prompt response");
                return Ok(new ChatResponse { Response = "Error processing response.", IsError = true });
            }
        }

        private async Task<IActionResult> HandleRegularChat(string userId, string userMessage, string? conversationId)
        {
            try
            {
                var history = await _conversationService.GetConversationHistory(userId, 5);
                var userStyle = await GetUserCommunicationStyle(userId);
                var aiPrompt = BuildRegularChatAIPrompt(userMessage, userId, history, userStyle);
                var aiResponse = await GetAIResponse(aiPrompt);
                
                await _conversationService.SaveConversation(userId, userMessage, aiResponse, conversationId);

                return Ok(new ChatResponse
                {
                    Response = aiResponse,
                    ConversationId = conversationId ?? Guid.NewGuid().ToString()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in regular chat");
                return Ok(new ChatResponse { Response = "Error in chat.", IsError = true });
            }
        }

        // All supporting methods from working baseline
        private async Task<string> BuildDailyPromptAIPrompt(string userMessage, DailyPromptSession session, string userId)
        {
            var userStyle = await GetUserCommunicationStyle(userId);
            var prompt = new StringBuilder();
            
            prompt.AppendLine("You are Chris Cooper, founder of Two-Brain Business. You're having a genuine conversation");
            prompt.AppendLine("with a business owner about their daily leadership reflection. Be real, be human, be helpful.");
            prompt.AppendLine("IMPORTANT: They may run any type of business - don't assume it's a gym or fitness business.");
            prompt.AppendLine("NEVER give out personal contact information, email addresses, or phone numbers.");
            prompt.AppendLine();
            prompt.AppendLine("CONTEXT:");
            prompt.AppendLine($"• Day {session.DayNumber} reflection topic: {session.DailyPrompt}");
            prompt.AppendLine($"• Their response: \"{userMessage}\"");
            
            if (session.ConversationHistory != null && session.ConversationHistory.Any())
            {
                prompt.AppendLine("• Previous conversation in this session:");
                foreach (var msg in session.ConversationHistory.TakeLast(3))
                {
                    prompt.AppendLine($"  - {msg}");
                }
            }
            
            prompt.AppendLine();
            prompt.AppendLine("HOW TO RESPOND:");
            prompt.AppendLine("• ACKNOWLEDGE what they've shared - show you truly heard them");
            prompt.AppendLine("• EMPATHIZE if they're facing challenges or celebrating wins");
            prompt.AppendLine("• Reference EXACTLY what they just said - use their own words");
            prompt.AppendLine("• Provide a brief insight, validation, or encouragement");
            prompt.AppendLine("• Be CONTEXT-AWARE - reference what they've shared so far");
            prompt.AppendLine("• Talk like you're sitting across from them, not giving a seminar");
            prompt.AppendLine();
            
            // Apply communication style
            if (userStyle == "short")
            {
                prompt.AppendLine("• ULTRA CONCISE: 3-8 words MAXIMUM total response");
                prompt.AppendLine("• Example: 'Good insight.' or 'That's normal, keep going.'");
                prompt.AppendLine("• NO explanations, NO examples, NO questions");
                prompt.AppendLine("• Shorter than a text message - extremely brief");
                prompt.AppendLine("• Just acknowledge in minimal words");
            }
            else if (userStyle == "long")
            {
                prompt.AppendLine("• Provide a thorough response - 4-6 sentences");
                prompt.AppendLine("• Include specific insights and examples");
                prompt.AppendLine("• Be comprehensive but still conversational");
            }
            else // medium (balanced) style
            {
                prompt.AppendLine("• BALANCED: EXACTLY 2-3 sentences MAXIMUM");
                prompt.AppendLine("• Example: 'I hear you. That makes sense. What's behind that?'");
                prompt.AppendLine("• Acknowledge + brief insight + one question MAX");
                prompt.AppendLine("• NO long explanations - keep it conversational but brief");
            }
            
            prompt.AppendLine();
            prompt.AppendLine("Respond as Chris Cooper - authentic, direct, supportive:");

            return prompt.ToString();
        }

        private string BuildRegularChatAIPrompt(string userMessage, string userId, List<Services.ConversationMessage> history, string userStyle)
        {
            var prompt = new StringBuilder();
            
            prompt.AppendLine("You are Chris Cooper, chatting with a business owner who trusts you.");
            prompt.AppendLine("They're on their leadership journey and need your guidance.");
            prompt.AppendLine("IMPORTANT: They may run any type of business - don't assume it's a gym or fitness business.");
            prompt.AppendLine("NEVER give out personal contact information, email addresses, or phone numbers.");
            prompt.AppendLine();
            
            if (history != null && history.Any())
            {
                prompt.AppendLine("CONVERSATION CONTEXT (recent messages):");
                foreach (var msg in history.TakeLast(3))
                {
                    prompt.AppendLine($"• {msg.Role}: {msg.Message}");
                }
                prompt.AppendLine();
            }
            
            prompt.AppendLine($"THEY JUST SAID: \"{userMessage}\"");
            prompt.AppendLine();
            
            // Apply communication style
            if (userStyle == "short")
            {
                prompt.AppendLine("• ULTRA CONCISE: 3-8 words MAXIMUM total response");
                prompt.AppendLine("• Example: 'Good insight.' or 'That's normal, keep going.'");
                prompt.AppendLine("• NO explanations, NO examples, NO questions");
                prompt.AppendLine("• Shorter than a text message - extremely brief");
                prompt.AppendLine("• Just acknowledge or validate in minimal words");
            }
            else if (userStyle == "long")
            {
                prompt.AppendLine("• Provide a thorough response - 4-6 sentences");
                prompt.AppendLine("• Include specific insights and examples");
                prompt.AppendLine("• Be comprehensive but still conversational");
            }
            else
            {
                prompt.AppendLine("• BALANCED: EXACTLY 2-3 sentences MAXIMUM");
                prompt.AppendLine("• Example: 'I hear you. That makes sense. What's behind that?'");
                prompt.AppendLine("• Acknowledge + brief insight + one question MAX");
                prompt.AppendLine("• NO long explanations - keep it conversational but brief");
            }
            
            prompt.AppendLine();
            prompt.AppendLine("Respond naturally as Chris would:");

            return prompt.ToString();
        }

        private async Task<string> GetAIResponse(string prompt)
        {
            try
            {
                var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _openAiApiKey);

                var requestBody = new
                {
                    model = "gpt-4o-mini",
                    messages = new[]
                    {
                        new { role = "system", content = prompt },
                        new { role = "user", content = "Please respond according to the instructions above." }
                    },
                    max_tokens = 500,
                    temperature = 0.7
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", content);
                
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("OpenAI API error: {StatusCode}", response.StatusCode);
                    return "I'm having trouble connecting right now. Can you try again?";
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<JsonElement>(responseContent);
                
                return result.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? 
                       "I'm having trouble responding right now. Can you try again?";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting AI response");
                return "I'm having trouble responding right now. Can you try again?";
            }
        }

        // Session clearing
        private async Task ClearAllActiveSessions(string userId)
        {
            try
            {
                if (_redis != null)
                {
                    var db = _redis.GetDatabase();
                    await db.KeyDeleteAsync($"daily_prompt_session:{userId}");
                    await db.KeyDeleteAsync($"burning_fires_session:{userId}");
                    await db.KeyDeleteAsync($"session:{userId}:active");
                    _logger.LogInformation("Cleared all active sessions for user {UserId}", userId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing sessions for user {UserId}", userId);
            }
        }

        // Daily prompt session methods (working baseline)
        private async Task<int> GetUserCurrentDay(string userId)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                
                var cmd = new NpgsqlCommand(@"
                    SELECT current_day FROM user_data WHERE user_id = @userId
                    UNION
                    SELECT current_day FROM user_preferences WHERE user_id = @userId::uuid
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

        private async Task StoreDailyPromptSession(string userId, int dayNumber, DailyPrompt dailyPrompt)
        {
            try
            {
                if (_redis?.IsConnected == true)
                {
                    var session = new DailyPromptSession
                    {
                        UserId = userId,
                        DayNumber = dayNumber,
                        DailyPrompt = dailyPrompt.PromptText,
                        CreatedAt = DateTime.UtcNow,
                        ConversationHistory = new List<string>()
                    };
                    
                    var sessionJson = JsonSerializer.Serialize(session);
                    var db = _redis.GetDatabase();
                    await db.StringSetAsync($"daily_prompt_session:{userId}", sessionJson, TimeSpan.FromHours(24));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error storing daily prompt session");
            }
        }

        private async Task<DailyPromptSession?> GetDailyPromptSession(string userId)
        {
            try
            {
                if (_redis?.IsConnected == true)
                {
                    var db = _redis.GetDatabase();
                    var sessionJson = await db.StringGetAsync($"daily_prompt_session:{userId}");
                    
                    if (sessionJson.HasValue && !string.IsNullOrEmpty(sessionJson))
                    {
                        return JsonSerializer.Deserialize<DailyPromptSession>(sessionJson!);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving daily prompt session");
            }
            return null;
        }

        private async Task UpdateDailyPromptSession(string userId, DailyPromptSession session)
        {
            try
            {
                if (_redis?.IsConnected == true)
                {
                    var db = _redis.GetDatabase();
                    var sessionJson = JsonSerializer.Serialize(session);
                    await db.StringSetAsync($"daily_prompt_session:{userId}", sessionJson, TimeSpan.FromHours(24));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating daily prompt session");
            }
        }

        private async Task CompleteDailyPrompt(string userId, int dayNumber)
        {
            try
            {
                if (_redis != null)
                {
                    var db = _redis.GetDatabase();
                    await db.KeyDeleteAsync($"daily_prompt_session:{userId}");
                }
                
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                
                await using var cmd = new NpgsqlCommand(@"
                    INSERT INTO user_prompt_deliveries (user_id, day_number, completed_at)
                    VALUES (@userId, @dayNumber, CURRENT_TIMESTAMP)
                    ON CONFLICT (user_id, day_number) DO UPDATE SET completed_at = CURRENT_TIMESTAMP", conn);
                
                cmd.Parameters.AddWithValue("userId", Guid.Parse(userId));
                cmd.Parameters.AddWithValue("dayNumber", dayNumber);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing daily prompt");
            }
        }

        private async Task ClearDailyPromptSession(string userId)
        {
            try
            {
                if (_redis != null)
                {
                    var db = _redis.GetDatabase();
                    await db.KeyDeleteAsync($"daily_prompt_session:{userId}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing daily prompt session");
            }
        }

        private async Task<string> GetUserCommunicationStyle(string userId)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                
                var cmd = new NpgsqlCommand(@"
                    SELECT communication_style FROM user_preferences 
                    WHERE user_id = @userId::uuid", conn);
                    
                cmd.Parameters.AddWithValue("userId", Guid.Parse(userId));
                
                var result = await cmd.ExecuteScalarAsync();
                return result?.ToString() ?? "medium";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user communication style");
                return "medium";
            }
        }

        private string GetAcknowledgmentResponse(string aiResponse)
        {
            var paragraphs = aiResponse.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
            return paragraphs.FirstOrDefault() ?? aiResponse;
        }

        private async Task<string> GetFollowUpWithKeyPhrases(DailyPromptSession session)
        {
            try
            {
                var prompt = "You are Chris Cooper. The user just shared their daily reflection and you acknowledged it. Now naturally mention that they have options to continue their leadership journey today. Mention in a conversational way that they can say 'done for the day' to finish, 'burning fires' for urgent issues, or 'Other Tinker Level Fires' for other challenges. Make it sound natural and supportive, not like a menu. Keep it to 2-3 sentences maximum.";
                
                var aiResponse = await GetAIResponse(prompt);
                
                if (!string.IsNullOrEmpty(aiResponse) && aiResponse.Length > 10)
                {
                    return aiResponse;
                }
                else
                {
                    return "You can wrap up when you're ready, or let me know if something urgent needs attention.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating natural key phrases");
                return "You can wrap up when you're ready, or let me know if something urgent needs attention.";
            }
        }

        private string GetTimeOfDay()
        {
            var hour = DateTime.Now.Hour;
            if (hour < 12) return "morning";
            if (hour < 18) return "afternoon";
            return "evening";
        }

        private string GetWelcomeMessage(string timeOfDay, string firstName)
        {
            var greetings = new Dictionary<string, string[]>
            {
                ["morning"] = new[] { $"Good morning, {firstName}! Ready to tackle today's leadership challenges?" },
                ["afternoon"] = new[] { $"Hey {firstName}, afternoon check-in. What's on your mind?" },
                ["evening"] = new[] { $"Good evening, {firstName}! Time to reflect on the day. What's on your mind?" }
            };
            
            var messages = greetings.ContainsKey(timeOfDay) ? greetings[timeOfDay] : greetings["afternoon"];
            var selectedMessage = messages[0];
            
            return selectedMessage + "\n\nYou can ask for your daily prompt, discuss burning fires, or just chat about leadership.";
        }

        private string GetCompletionMessage(int dayNumber, string userStyle)
        {
            var baseMessage = $"Excellent work completing Day {dayNumber} of your leadership journey! You're building real self-awareness.";
            
            if (userStyle == "short")
            {
                return "Great job! Day complete.";
            }
            else if (userStyle == "long")
            {
                return baseMessage + " Taking time for daily reflection is one of the most powerful habits successful leaders develop. You're investing in your own growth, which directly impacts your ability to lead others effectively. Tomorrow we'll continue building on this foundation.";
            }
            else
            {
                return baseMessage + " Come back tomorrow for Day " + (dayNumber + 1) + "!";
            }
        }
    }

    // Supporting models
    public class ChatRequest
    {
        public string Message { get; set; } = "";
        public string? ConversationId { get; set; }
        public string? UserId { get; set; }
        public string? UserName { get; set; }
        public string? BusinessName { get; set; }
        public int? CurrentDay { get; set; }
    }

    public class ChatResponse
    {
        public string Response { get; set; } = "";
        public string? ConversationId { get; set; }
        public bool IsError { get; set; }
        public bool IsDailyPrompt { get; set; }
        public int? DayNumber { get; set; }
        public bool IsDailyPromptComplete { get; set; }
        public bool IsBurningFires { get; set; }
        public bool IsSessionEnd { get; set; }
    }

    public class DailyPromptSession
    {
        public string UserId { get; set; } = "";
        public int DayNumber { get; set; }
        public string DailyPrompt { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public List<string>? ConversationHistory { get; set; }
    }

    public class DailyPrompt
    {
        public string PromptTitle { get; set; } = "";
        public string PromptText { get; set; } = "";
        public int DayNumber { get; set; }
    }

    public class BurningFiresSession
    {
        public string UserId { get; set; } = "";
        public string ConversationId { get; set; } = "";
        public string InitialIssue { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public List<string>? ConversationHistory { get; set; }
    }
}

