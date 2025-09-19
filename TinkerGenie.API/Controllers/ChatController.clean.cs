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
        private readonly ICurriculumSearchService _curriculumSearchService;
        private readonly IConnectionMultiplexer? _redis;
        private readonly HttpClient _httpClient;
        private readonly string _openAiApiKey;
        private readonly string _connectionString;

        public ChatController(
            IConfiguration configuration, 
            ILogger<ChatController> logger, 
            IConversationService conversationService,
            ICurriculumSearchService curriculumSearchService,
            IConnectionMultiplexer? redis = null,
            IWeaviateService? weaviateService = null)
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

        // USER REQUESTED: New Chat endpoint for session clearing
        [HttpPost("new")]
        public async Task<IActionResult> NewChat()
        {
            try
            {
                var userId = User.FindFirst("userId")?.Value ?? "";
                var name = User.FindFirst("name")?.Value ?? "Leader";
                
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new { success = false, message = "Invalid user" });
                }
                
                _logger.LogInformation("New chat requested by user: {UserId}", userId);
                
                // Clear Redis sessions
                await ClearAllActiveSessions(userId);
                
                // Generate new conversation ID
                var newConversationId = Guid.NewGuid().ToString();
                
                // Time-based welcome message
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
                return Ok(new
                {
                    success = false,
                    error = "Failed to create new chat session",
                    message = "Let's start fresh. What would you like to discuss today?"
                });
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
                var businessName = User.FindFirst("businessName")?.Value ?? "Your Business";
                
                _logger.LogInformation("Chat request from user: {UserId}, message: {Message}", userId, request.Message);

                // Check if this is a daily prompt request
                if (IsDailyPromptRequest(request.Message))
                {
                    return await HandleDailyPromptRequest(userId, firstName, businessName, request.ConversationId, request.Message);
                }

                // Check if user is in a daily prompt session
                var dailyPromptSession = await GetDailyPromptSession(userId);
                if (dailyPromptSession != null)
                {
                    return await HandleDailyPromptResponse(userId, request.Message, dailyPromptSession, request.ConversationId);
                }

                // Check for burning fires scenario
                if (IsBurningFiresScenario(request.Message))
                {
                    return await HandleBurningFiresRequest(userId, request.Message, request.ConversationId);
                }

                // Regular chat - use AI for everything
                return await HandleRegularChat(userId, request.Message, request.ConversationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in chat endpoint for user: {UserId}", request.UserId);
                
                var errorResponse = await GetAIResponse($"User encountered an error: {ex.Message}. Provide a supportive response as Chris Cooper would, acknowledging the technical difficulty but maintaining focus on their leadership development.");
                
                return Ok(new ChatResponse
                {
                    Response = errorResponse,
                    ConversationId = request.ConversationId ?? Guid.NewGuid().ToString(),
                    IsError = true
                });
            }
        }

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

        // ISSUE #1 FIX: Ensure daily prompt request saves conversation for sidebar
        private async Task<IActionResult> HandleDailyPromptRequest(string userId, string firstName, string businessName, string? conversationId, string? originalMessage = null)
        {
            try
            {
                var isStartFresh = originalMessage?.ToLower().Contains("start fresh") == true;
                
                var existingSession = await GetDailyPromptSession(userId);
                if (existingSession != null && !isStartFresh)
                {
                    _logger.LogInformation("User {UserId} already has active daily prompt session for day {Day}", userId, existingSession.DayNumber);
                    return Ok(new ChatResponse
                    {
                        Response = $"You have an active Daily Prompt session for Day {existingSession.DayNumber}. Type 'start fresh' to reset and begin a new prompt, or continue with your current reflection.",
                        IsDailyPrompt = true,
                        DayNumber = existingSession.DayNumber,
                        IsDailyPromptComplete = false,
                        ConversationId = conversationId ?? Guid.NewGuid().ToString()
                    });
                }
                
                if (existingSession != null && isStartFresh)
                {
                    _logger.LogInformation("User {UserId} requested fresh start, clearing session for day {Day}", userId, existingSession.DayNumber);
                    await ClearDailyPromptSession(userId);
                }

                var currentDay = await GetUserCurrentDay(userId);
                var dailyPrompt = await GetDailyPromptFromDatabase(currentDay);
                if (dailyPrompt == null)
                {
                    return Ok(new ChatResponse
                    {
                        Response = "ERROR - Daily Prompt API Failed. Status: 500. Error: No daily prompt available in database. Please check the API connection and try again.",
                        IsError = true,
                        ConversationId = conversationId ?? Guid.NewGuid().ToString()
                    });
                }

                await StoreDailyPromptSession(userId, currentDay, dailyPrompt);

                _logger.LogInformation("Delivering EXACT prompt from DB - Title: {Title}, Text: {Text}", 
                    dailyPrompt.PromptTitle, dailyPrompt.PromptText);
                
                var response = dailyPrompt.PromptText;
                
                // CRITICAL FOR ISSUE #1: Save the initial prompt delivery as a conversation so it appears in sidebar
                var finalConversationId = conversationId ?? Guid.NewGuid().ToString();
                await _conversationService.SaveConversation(userId, "START_DAILY_PROMPT", response, finalConversationId);
                _logger.LogInformation("ISSUE #1 FIX: Saved initial daily prompt delivery to conversation history for user {UserId}", userId);

                return Ok(new ChatResponse
                {
                    Response = response,
                    IsDailyPrompt = true,
                    DayNumber = currentDay,
                    IsDailyPromptComplete = false,
                    ConversationId = finalConversationId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling daily prompt request for user {UserId}", userId);
                return Ok(new ChatResponse
                {
                    Response = "ERROR - Daily Prompt API Failed. Status: 500. Error: Database connection failed. Please check the API connection and try again.",
                    IsError = true,
                    ConversationId = conversationId ?? Guid.NewGuid().ToString()
                });
            }
        }

        private async Task<IActionResult> HandleDailyPromptResponse(string userId, string userMessage, DailyPromptSession session, string? conversationId)
        {
            try
            {
                if (session.ConversationHistory == null)
                    session.ConversationHistory = new List<string>();
                session.ConversationHistory.Add(userMessage);
                
                var lowerMessage = userMessage.ToLower();
                
                if (lowerMessage.Contains("done for the day"))
                {
                    await CompleteDailyPrompt(userId, session.DayNumber);
                    
                    var userStyle = await GetUserCommunicationStyle(userId);
                    var completionMessage = GetCompletionMessage(session.DayNumber, userStyle);
                    
                    // ISSUE #1: Save completion conversation for sidebar
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
                
                if (lowerMessage.Contains("burning fires") || lowerMessage.Contains("urgent"))
                {
                    var specialPrompt = await BuildBurningFiresGuidancePrompt(userMessage, new List<string>(), userId);
                    var specialResponse = await GetAIResponse(specialPrompt);
                    
                    // ISSUE #1: Save burning fires conversation for sidebar
                    await _conversationService.SaveConversation(userId, userMessage, specialResponse, conversationId);
                    
                    return Ok(new ChatResponse
                    {
                        Response = specialResponse,
                        IsBurningFires = true,
                        ConversationId = conversationId ?? Guid.NewGuid().ToString()
                    });
                }
                
                var aiPrompt = await BuildDailyPromptAIPrompt(userMessage, session, userId);
                var aiResponse = await GetAIResponse(aiPrompt);
                
                var communicationStyle = await GetUserCommunicationStyle(userId);
                var acknowledgment = GetAcknowledgmentResponse(aiResponse);
                var followUp = await GetFollowUpWithKeyPhrases(session);
                var fullResponse = acknowledgment + "|SPLIT|" + followUp;
                
                // ISSUE #1: Save conversation for sidebar
                await _conversationService.SaveConversation(userId, userMessage, fullResponse, conversationId);

                await UpdateDailyPromptSession(userId, session);

                return Ok(new ChatResponse
                {
                    Response = fullResponse,
                    IsDailyPrompt = true,
                    DayNumber = session.DayNumber,
                    IsDailyPromptComplete = false,
                    ConversationId = conversationId ?? Guid.NewGuid().ToString()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling daily prompt response for user {UserId}", userId);
                return Ok(new ChatResponse
                {
                    Response = "ERROR - Daily Prompt API Failed. Status: 500. Error: Database connection failed. Please check the API connection and try again.",
                    IsError = true,
                    ConversationId = conversationId ?? Guid.NewGuid().ToString()
                });
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
                
                // ISSUE #1: Save regular chat conversation for sidebar
                await _conversationService.SaveConversation(userId, userMessage, aiResponse, conversationId);

                return Ok(new ChatResponse
                {
                    Response = aiResponse,
                    ConversationId = conversationId ?? Guid.NewGuid().ToString()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling regular chat for user {UserId}", userId);
                return Ok(new ChatResponse
                {
                    Response = "ERROR - Daily Prompt API Failed. Status: 500. Error: Database connection failed. Please check the API connection and try again.",
                    IsError = true,
                    ConversationId = conversationId ?? Guid.NewGuid().ToString()
                });
            }
        }

        private async Task<IActionResult> HandleBurningFiresRequest(string userId, string userMessage, string? conversationId)
        {
            try
            {
                // CRITICAL: Create NEW conversation ID for burning fires (separate sidebar entry)
                var burningFiresConversationId = Guid.NewGuid().ToString();
                _logger.LogInformation("Starting new burning fires session for user {UserId} with conversation {ConversationId}", userId, burningFiresConversationId);
                
                // Check if this is the initial "burning fires" trigger or a follow-up question
                if (userMessage.ToLower().Trim() == "burning fires")
                {
                    // Initial trigger - ask discovery questions to understand the issue
                    var discoveryResponse = "I understand you have some urgent issues that need attention. To help you find the best solution from our knowledge base, I need to understand what's happening.\n\nWhat specifically is the burning fire you're dealing with right now? Is it a cash flow issue, staff problem, customer situation, or something else?";
                    
                    // Save initial burning fires conversation to create new sidebar entry
                    await _conversationService.SaveConversation(userId, userMessage, discoveryResponse, burningFiresConversationId);
                    
                    return Ok(new ChatResponse
                    {
                        Response = discoveryResponse,
                        IsBurningFires = true,
                        ConversationId = burningFiresConversationId
                    });
                }
                else
                {
                    // USER DESCRIBED THE ISSUE - Search knowledge base and provide solutions with clickable links
                    var searchResult = await _curriculumSearchService.SearchForBurningFires(userMessage);
                    var solutionResponse = await BuildBurningFiresSolutionWithLinks(userMessage, searchResult, userId);
                    
                    // Save solution conversation
                    await _conversationService.SaveConversation(userId, userMessage, solutionResponse, burningFiresConversationId);
                    
                    return Ok(new ChatResponse
                    {
                        Response = solutionResponse,
                        IsBurningFires = true,
                        ConversationId = burningFiresConversationId
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling burning fires for user {UserId}", userId);
                return Ok(new ChatResponse
                {
                    Response = "I understand you have an urgent issue. Can you tell me specifically what's happening so I can help you find the right solution?",
                    IsBurningFires = true,
                    ConversationId = Guid.NewGuid().ToString()
                });
            }
        }

        private bool IsBurningFiresScenario(string message)
        {
            var urgentPatterns = new[] {
                "burning fire", "burning fires", "urgent", "emergency", "crisis",
                "immediate help", "need help now", "urgent situation"
            };
            
            var lowerMessage = message?.ToLower() ?? "";
            return urgentPatterns.Any(pattern => lowerMessage.Contains(pattern));
        }

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
            prompt.AppendLine("IMPORTANT - DO NOT:");
            prompt.AppendLine("• Don't ask additional probing questions unless truly necessary");
            prompt.AppendLine("• Don't change the subject or redirect");
            prompt.AppendLine("• Don't sound like a therapist or coach");
            prompt.AppendLine("• Don't give generic business advice");
            prompt.AppendLine("• Do NOT say 'tell me more' or similar");
            prompt.AppendLine("• Just acknowledge, validate, and provide brief insight");
            prompt.AppendLine();
            prompt.AppendLine("LENGTH:");
            
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
            prompt.AppendLine("YOUR STYLE:");
            prompt.AppendLine("• Talk like you're having coffee together");
            prompt.AppendLine("• Share what's worked for other business owners (be specific)");
            prompt.AppendLine("• Give them one clear thing to try");
            prompt.AppendLine("• Ask what's really behind their question");
            
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
            else // medium (balanced) style  
            {
                prompt.AppendLine("• BALANCED: EXACTLY 2-3 sentences MAXIMUM");
                prompt.AppendLine("• Example: 'I hear you. That makes sense. What's behind that?'");
                prompt.AppendLine("• Acknowledge + brief insight + one question MAX");
                prompt.AppendLine("• NO long explanations - keep it conversational but brief");
            }
            
            prompt.AppendLine();
            prompt.AppendLine("REMEMBER:");
            prompt.AppendLine("• Use their exact words when you reference their situation");
            prompt.AppendLine("• If they mention numbers/specifics, use them");
            prompt.AppendLine("• Don't sound like a textbook - sound like Chris");
            prompt.AppendLine("• Be CONTEXT-AWARE - reference what they've shared before");
            prompt.AppendLine("• End with curiosity about their specific situation");
            prompt.AppendLine();
            prompt.AppendLine("FORMAT:");
            prompt.AppendLine("• Bullet points (•) for lists");
            prompt.AppendLine("• Line breaks for clarity");
            prompt.AppendLine("• Numbers (1. 2. 3.) only for steps");
            prompt.AppendLine();
            prompt.AppendLine("Respond naturally as Chris would:");

            return prompt.ToString();
        }

        // CORRECT BURNING FIRES: Solution with clickable links
        private async Task<string> BuildBurningFiresSolutionWithLinks(string userIssue, CurriculumSearchResponse searchResult, string userId)
        {
            var userStyle = await GetUserCommunicationStyle(userId);
            
            var solution = new StringBuilder();
            
            // Chris Cooper style response with empathy
            if (userStyle == "short")
            {
                solution.AppendLine("Found relevant resources for your situation:");
            }
            else if (userStyle == "long")
            {
                solution.AppendLine($"I understand this '{userIssue}' situation is putting pressure on you right now. I've found some specific resources from our knowledge base that directly address this type of issue. These have helped other business owners in similar situations.");
            }
            else
            {
                solution.AppendLine($"I hear you on this '{userIssue}' issue. Let me share some resources that have helped other business owners facing similar challenges.");
            }
            
            solution.AppendLine();
            
            if (searchResult.Resources != null && searchResult.Resources.Any())
            {
                solution.AppendLine("**Recommended Solutions:**");
                solution.AppendLine();
                
                foreach (var resource in searchResult.Resources.Take(3))
                {
                    solution.AppendLine($"📋 **{resource.Title}**");
                    solution.AppendLine($"💡 {resource.Preview}");
                    solution.AppendLine($"🔗 [Click here to read the full solution]({resource.Url})");
                    solution.AppendLine();
                }
                
                // Add recommendation based on best match
                var topResource = searchResult.Resources.First();
                if (userStyle == "short")
                {
                    solution.AppendLine($"**My recommendation**: Start with '{topResource.Title}' - it's most relevant to your situation.");
                }
                else
                {
                    solution.AppendLine($"**My recommendation**: I'd start with '{topResource.Title}' as it's most directly applicable to your situation. This approach has worked well for other business owners I've mentored who faced similar challenges.");
                }
            }
            else
            {
                // Fallback if no specific resources found
                solution.AppendLine("While I search for specific resources on this issue, here's what I'd recommend as an immediate first step:");
                solution.AppendLine($"🔗 [General Business Crisis Management Framework](https://twobrain.com/crisis-management)");
            }
            
            solution.AppendLine();
            solution.AppendLine("Click any link above to open the full resource in a new window. Which of these approaches resonates most with your situation?");
            
            return solution.ToString();
        }
        
        // CORRECT BURNING FIRES: Knowledge Base Search
        private async Task<List<string>> SearchKnowledgeBaseForBurningFire(string issueDescription, string userId)
        {
            try
            {
                if (_weaviateService != null)
                {
                    var searchResults = await _weaviateService.SearchCurriculum(issueDescription, 3);
                    if (searchResults != null && searchResults.Any())
                    {
                        return searchResults.Select(r => $"{r.Title}: {r.Content}").ToList();
                    }
                }
                
                // Fallback knowledge base entries if Weaviate search fails
                return new List<string>
                {
                    "Cash Flow Issues: Focus on collecting outstanding invoices and setting up payment terms",
                    "Staff Problems: Address conflicts directly with one-on-one conversations",
                    "Customer Complaints: Turn complaints into improvement opportunities"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching knowledge base for burning fires");
                return new List<string> { "General Business Issue Resolution Framework available" };
            }
        }
        
        // CORRECT BURNING FIRES: Guidance Based on Knowledge Base
        private async Task<string> BuildBurningFiresGuidancePrompt(string userIssue, List<string> knowledgeBaseResults, string userId)
        {
            var userStyle = await GetUserCommunicationStyle(userId);
            
            var prompt = new StringBuilder();
            prompt.AppendLine("You are Chris Cooper. The business owner has described their burning fire issue.");
            prompt.AppendLine("You have relevant knowledge base resources to help them.");
            prompt.AppendLine("Provide guidance based on the knowledge base, but make it conversational and supportive.");
            prompt.AppendLine();
            prompt.AppendLine($"THEIR ISSUE: \"{userIssue}\"");
            prompt.AppendLine();
            prompt.AppendLine("RELEVANT KNOWLEDGE BASE RESOURCES:");
            foreach (var resource in knowledgeBaseResults.Take(3))
            {
                prompt.AppendLine($"• {resource}");
            }
            prompt.AppendLine();
            prompt.AppendLine("YOUR RESPONSE SHOULD:");
            prompt.AppendLine("• Reference the relevant knowledge base resource naturally");
            prompt.AppendLine("• Give them one specific next step from the resource");
            prompt.AppendLine("• Ask if they need more detail on any specific aspect");
            prompt.AppendLine("• Be supportive - they're stressed about this issue");
            prompt.AppendLine();
            
            if (userStyle == "short")
            {
                prompt.AppendLine("• Keep guidance concise: 1-2 sentences + question");
            }
            else if (userStyle == "long")
            {
                prompt.AppendLine("• Provide detailed guidance: 4-5 sentences with examples");
            }
            else
            {
                prompt.AppendLine("• Balanced guidance: 2-3 sentences + question");
            }
            
            prompt.AppendLine();
            prompt.AppendLine("Respond as Chris Cooper with knowledge-based guidance:");

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

        // All the supporting methods for daily prompt functionality
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

        // USER REQUESTED: Session clearing functionality
        private async Task ClearAllActiveSessions(string userId)
        {
            try
            {
                if (_redis != null)
                {
                    var db = _redis.GetDatabase();
                    await db.KeyDeleteAsync($"daily_prompt_session:{userId}");
                    await db.KeyDeleteAsync($"session:{userId}:active");
                    await db.KeyDeleteAsync($"burning_fire_session:{userId}");
                    await db.KeyDeleteAsync($"general_chat_session:{userId}");
                    _logger.LogInformation("Cleared all active sessions for user {UserId}", userId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing all sessions for user {UserId}", userId);
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
                    _logger.LogInformation("Cleared Redis daily prompt session for user {UserId}", userId);
                }
                _logger.LogInformation("Daily prompt session cleared for user {UserId} - fresh start enabled", userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing daily prompt session for user {UserId}", userId);
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
                ["morning"] = new[]
                {
                    $"Good morning, {firstName}! Ready to tackle today's leadership challenges?",
                    $"Morning, {firstName}! What's on your mind as we start the day?",
                    $"Hey {firstName}, good morning! Let's make today count. What can I help you with?"
                },
                ["afternoon"] = new[]
                {
                    $"Good afternoon, {firstName}! How's your day going so far?",
                    $"Hey {firstName}, afternoon check-in. What's on your mind?",
                    $"Afternoon, {firstName}! Let's tackle whatever's on your plate."
                },
                ["evening"] = new[]
                {
                    $"Good evening, {firstName}! Time to reflect on the day. What's on your mind?",
                    $"Evening, {firstName}! Let's wrap up any loose ends from today.",
                    $"Hey {firstName}, evening! How can I help you wind down the day?"
                }
            };
            
            var messages = greetings.ContainsKey(timeOfDay) ? greetings[timeOfDay] : greetings["afternoon"];
            var random = new Random();
            var selectedMessage = messages[random.Next(messages.Length)];
            
            return selectedMessage + "\n\nYou can ask for your daily prompt, discuss burning fires, or just chat about leadership.";
        }

        // Daily prompt session management
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
                _logger.LogError(ex, "Error storing daily prompt session in Redis");
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
                
                _logger.LogInformation("Completed daily prompt for user {UserId}, day {DayNumber}", userId, dayNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing daily prompt for user {UserId}", userId);
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
                _logger.LogError(ex, "Error getting user communication style for user {UserId}", userId);
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
                    _logger.LogInformation("Generated natural key phrases: {Response}", aiResponse);
                    return aiResponse;
                }
                else
                {
                    _logger.LogWarning("AI key phrase generation failed, using natural fallback");
                    return "You can wrap up when you're ready, or let me know if something urgent needs attention - those burning fires we can tackle together.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating natural key phrases, using fallback");
                return "You can wrap up when you're ready, or let me know if something urgent needs attention.";
            }
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
}
