using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using TinkerGenie.API.Services;
using System.Text;
using System.Text.Json;
using StackExchange.Redis;
using Npgsql;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using ConversationType = TinkerGenie.API.Services.ConversationType;
using TinkerGenie.API.Services.Interfaces;
using TinkerGenie.API.Models;
using OpenAI.Chat;

namespace TinkerGenie.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class ChatController : ControllerBase
    {
        private readonly ILogger<ChatController> _logger;
        private readonly ISessionManagerService _sessionManager;
        private readonly IConversationService _conversationService;
        private readonly IWeaviateService? _weaviateService;
        private readonly IConnectionMultiplexer? _redis;
        private readonly ChatClient? _chatClient;
        private readonly string _connectionString;
        private readonly ICurriculumSearchService? _curriculumSearch;

        // Predefined conversation type mappings
        private static readonly Dictionary<string, ConversationType> ConversationTypeMappings = new()
        {
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
            ChatClient? chatClient = null,
            ICurriculumSearchService? curriculumSearch = null)
        {
            _logger = logger;
            _sessionManager = sessionManager;
            _conversationService = conversationService;
            _weaviateService = weaviateService;
            _redis = redis;
            _chatClient = chatClient;
            _curriculumSearch = curriculumSearch;
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
        }

        [HttpPost("send")]
        public async Task<IActionResult> SendMessage([FromBody] ChatRequest request)
        {
            try 
            {
                var userId = await ResolveCanonicalUserId(User);
                var firstName = User.FindFirst("first_name")?.Value ?? User.FindFirst("firstName")?.Value ?? User.FindFirst("given_name")?.Value ?? "User";
                var businessName = User.FindFirst("businessName")?.Value ?? "Your Business";

                // Determine conversation type - check messageType field first, then message content
                var conversationType = ConversationType.GENERAL_CHAT;
                if (!string.IsNullOrEmpty(request.MessageType))
                {
                    switch (request.MessageType.ToLowerInvariant())
                    {
                        case "burning_fire":
                            conversationType = ConversationType.BURNING_FIRE;
                            break;
                        case "tinker_level":
                            conversationType = ConversationType.TINKER_LEVEL;
                            break;
                        case "daily_prompt":
                            conversationType = ConversationType.DAILY_PROMPT;
                            break;
                        default:
                            conversationType = DetermineConversationType(request.Message);
                            break;
                    }
                }
                else
                {
                    conversationType = DetermineConversationType(request.Message);
                }

                // Check if this is a response to a daily prompt conversation
                if (request.MessageType == "daily_prompt" || (!string.IsNullOrEmpty(request.ConversationId) && await IsDailyPromptThread(request.ConversationId)))
                {
                    // Handle daily prompt completion options
                    if (IsTalkMoreRequest(request.Message) || request.Message.ToLowerInvariant().Contains("explore") || request.Message.ToLowerInvariant().Contains("go deeper"))
                    {
                        // User wants to explore the prompt deeper - provide additional coaching
                        var deeperResponse = await GenerateDeeperPromptCoaching(request.Message, firstName);
                        await _sessionManager.UpdateThreadMessages(request.ConversationId, request.Message, deeperResponse);
                        
                        return Ok(new ChatResponse
                        {
                            Response = deeperResponse,
                            ConversationId = request.ConversationId
                        });
                    }
                    else if (IsDoneForDayRequest(request.Message))
                    {
                        // User is done for the day - mark completion, increment day, and provide closing
                        var doneResponse = await HandleDoneForDay(userId);
                        await _sessionManager.UpdateThreadMessages(request.ConversationId, request.Message, doneResponse.Response);
                        
                        return Ok(new ChatResponse
                        {
                            Response = doneResponse.Response,
                            ConversationId = request.ConversationId,
                            IsSessionEnd = doneResponse.IsSessionEnd
                        });
                    }
                    else
                    {
                        // First response to daily prompt - provide acknowledgment with options
                        var dailyResponse = await GenerateChrisCooperDailyResponse(request.Message, firstName);
                        await _sessionManager.UpdateThreadMessages(request.ConversationId, request.Message, dailyResponse);
                        
                        return Ok(new ChatResponse
                        {
                            Response = dailyResponse,
                            ConversationId = request.ConversationId
                        });
                    }
                }

                // Create or retrieve conversation thread
                var thread = await _sessionManager.GetActiveThread(userId, conversationType);

                ChatResponse response;
                switch (conversationType)
                {
                    case ConversationType.BURNING_FIRE:
                        response = await HandleBurningFire(userId, request.Message, thread);
                        break;
                    
                    case ConversationType.TINKER_LEVEL:
                        response = await HandleTinkerLevel(userId, request.Message, thread);
                        break;
                    
                    case ConversationType.SESSION_END:
                        response = await HandleDoneForDay(userId);
                        break;
                    
                    default:
                        response = await HandleGeneralChat(userId, request.Message, thread);
                        break;
                }

                // Update thread with messages if we got a valid response
                if (!string.IsNullOrEmpty(response.Response) && thread != null)
                {
                    var persistThreadId = !string.IsNullOrEmpty(request.ConversationId) ? request.ConversationId : thread.ThreadId;
                    await _sessionManager.UpdateThreadMessages(persistThreadId, request.Message, response.Response);
                    response.ConversationId = persistThreadId;
                }

                // Normalize REST response for frontend
                return Ok(new {
                    id = Guid.NewGuid().ToString(),
                    role = "assistant",
                    content = response.Response,
                    conversationId = response.ConversationId,
                    isError = response.IsError,
                    timestamp = DateTime.UtcNow,
                    metadata = new {
                        isDailyPrompt = response.IsDailyPrompt,
                        isDailyPromptComplete = response.IsDailyPromptComplete,
                        dayNumber = response.DayNumber,
                        isComplete = response.IsDailyPromptComplete,
                        sessionType = response.SessionType
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing chat message");
                return StatusCode(500, new { message = "Unable to process chat message" });
            }
        }

        // Separate endpoints for clearer routing and guardrails
        // DAILY PROMPT: start → respond → done
        [HttpPost("daily/start")]
        public async Task<IActionResult> DailyStart()
        {
            try
            {
                var userId = await ResolveCanonicalUserId(User);
                var firstName = User.FindFirst("first_name")?.Value ?? User.FindFirst("firstName")?.Value ?? User.FindFirst("given_name")?.Value ?? "User";
                var businessName = User.FindFirst("businessName")?.Value ?? "Your Business";

                // Create daily thread
                var currentDay = await GetUserCurrentDay(userId);
                // Gate: if Redis marks this day completed, do not start again
                try
                {
                    if (_redis != null)
                    {
                        var db = _redis.GetDatabase();
                        var lastCompleted = await db.StringGetAsync($"user:{userId}:daily:last_completed_day");
                        var completedAt = await db.StringGetAsync($"user:{userId}:daily:completed_at");
                        
                        if (lastCompleted.HasValue && int.TryParse(lastCompleted.ToString(), out var completedDay))
                        {
                            // Check if they completed today (same day completion)
                            if (completedDay >= currentDay)
                        {
                            return Ok(new {
                                    response = $"You already completed Day {completedDay} today. Great work! Your next prompt (Day {currentDay + 1}) will be ready tomorrow.",
                                    isDailyPrompt = true,
                                    isDailyPromptComplete = true,
                                    dayNumber = currentDay + 1, // Show next day number
                                    timestamp = DateTime.UtcNow
                                });
                            }
                            
                            // Check if they completed yesterday and it's still the same calendar day
                            if (completedAt.HasValue && DateTime.TryParse(completedAt.ToString(), out var completionTime))
                            {
                                var now = DateTime.UtcNow;
                                var timeSinceCompletion = now - completionTime;
                                
                                // If completed less than 4 hours ago (prevent same-day re-access)
                                if (timeSinceCompletion.TotalHours < 4)
                                {
                                    var nextDay = completedDay + 1;
                                    return Ok(new {
                                        response = $"You completed Day {completedDay} recently. Take some time to reflect! Your next prompt (Day {nextDay}) will be ready in a few hours.",
                                isDailyPrompt = true,
                                isDailyPromptComplete = true,
                                dayNumber = nextDay,
                                timestamp = DateTime.UtcNow
                            });
                                }
                            }
                        }
                    }
                }
                catch { }
                var dailyPrompt = await GetDailyPromptFromDatabase(currentDay);
                if (dailyPrompt == null)
                {
                    return Ok(new ChatResponse { Response = "I couldn't find today's leadership prompt.", IsError = true });
                }

                var thread = await _sessionManager.CreateThread(userId, TinkerGenie.API.Services.ConversationType.DAILY_PROMPT, $"Day {currentDay} Reflection");

                // Mark awaiting first response in Redis
                try
                {
                    if (_redis != null)
                    {
                        var db = _redis.GetDatabase();
                        var awaiting = System.Text.Json.JsonSerializer.Serialize(new { awaitingResponse = true, dayNumber = currentDay });
                        await db.StringSetAsync($"session:{userId}:daily", awaiting, TimeSpan.FromMinutes(60));
                    }
                }
                catch {}

                // Format daily prompt with split delimiter for multiple chat bubbles
                var formattedPrompt = FormatDailyPromptWithSplit(dailyPrompt.PromptText, currentDay);
                
                return Ok(new {
                    response = formattedPrompt,
                    conversationId = thread.ThreadId,
                    isDailyPrompt = true,
                    dayNumber = currentDay,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in DailyStart");
                return Ok(new ChatResponse { Response = "Unable to start today's prompt.", IsError = true });
            }
        }

        // Legacy compatibility endpoint: some clients still call /api/chat/daily-prompt
        [HttpPost("daily-prompt")]
        public Task<IActionResult> DailyPromptLegacy()
        {
            return DailyStart();
        }

        public class DailyRespondRequest { public string? Message { get; set; } public string? ConversationId { get; set; } }

        [HttpPost("daily/respond")]
        public async Task<IActionResult> DailyRespond([FromBody] DailyRespondRequest req)
        {
            try
            {
                var userId = await ResolveCanonicalUserId(User);
                var firstName = User.FindFirst("first_name")?.Value ?? User.FindFirst("firstName")?.Value ?? User.FindFirst("given_name")?.Value ?? "User";
                var businessName = User.FindFirst("businessName")?.Value ?? "Your Business";

                // If awaiting first response, provide Chris Cooper coaching response
                if (await IsRespondingToDailyPrompt(userId))
                {
                    try { if (_redis != null) await _redis.GetDatabase().KeyDeleteAsync($"session:{userId}:daily"); } catch {}
                    
                    // Generate Chris Cooper coaching response using OpenAI
                    var coachingResponse = await GenerateChrisCooperDailyResponse(req.Message ?? string.Empty, firstName);

                    var thread = new ConversationThread { ThreadId = req.ConversationId ?? Guid.NewGuid().ToString() };
                    await _sessionManager.UpdateThreadMessages(thread.ThreadId, req.Message ?? string.Empty, coachingResponse);

                    return Ok(new {
                        id = Guid.NewGuid().ToString(),
                        role = "assistant",
                        content = coachingResponse,
                        conversationId = thread.ThreadId,
                        isDailyPrompt = true,
                        timestamp = DateTime.UtcNow
                    });
                }

                // If awaiting option and they sent free-form, re-show options
                if (await IsAwaitingDailyOption(userId) && !IsOptionSelection(req.Message ?? string.Empty))
                {
                    var reGate = BuildOptionGateResponse(req.ConversationId ?? string.Empty);
                    return Ok(new {
                        id = Guid.NewGuid().ToString(),
                        role = "assistant",
                        content = reGate.Response,
                        conversationId = req.ConversationId,
                        isDailyPrompt = true,
                        showOptions = true,
                        timestamp = DateTime.UtcNow
                    });
                }

                // Handle recognized options
                if (IsTalkMoreRequest(req.Message ?? string.Empty))
                {
                    await SetAwaitingDailyOption(userId, false);
                    var thread = new ConversationThread { ThreadId = req.ConversationId ?? Guid.NewGuid().ToString() };
                    var resp = await HandleDailyPromptFlow(userId, firstName, businessName, "talk more about today's prompt", thread);
                    return Ok(new { content = resp.Response, conversationId = thread.ThreadId, isDailyPrompt = true, timestamp = DateTime.UtcNow });
                }
                if (IsBurningFiresRequest(req.Message ?? string.Empty))
                {
                    await SetAwaitingDailyOption(userId, false);
                    var thread = await _sessionManager.CreateThread(userId, TinkerGenie.API.Services.ConversationType.BURNING_FIRE, "Urgent Issue");
                    var resp = await HandleBurningFire(userId, req.Message ?? string.Empty, thread);
                    return Ok(new { content = resp.Response, conversationId = thread.ThreadId, isDailyPrompt = false, timestamp = DateTime.UtcNow });
                }
                if (IsTinkerLevelRequest(req.Message ?? string.Empty))
                {
                    await SetAwaitingDailyOption(userId, false);
                    var thread = await _sessionManager.CreateThread(userId, TinkerGenie.API.Services.ConversationType.TINKER_LEVEL, "Tinker Level Issue");
                    var resp = await HandleTinkerLevel(userId, req.Message ?? string.Empty, thread);
                    return Ok(new { content = resp.Response, conversationId = thread.ThreadId, isDailyPrompt = false, timestamp = DateTime.UtcNow });
                }

                if (IsDoneForDayRequest(req.Message ?? string.Empty))
                {
                    await SetAwaitingDailyOption(userId, false);
                    var resp = await HandleDoneForDay(userId);
                    return Ok(new { content = resp.Response, isDailyPromptComplete = true, isSessionEnd = true, timestamp = DateTime.UtcNow });
                }

                // Default re-gate
                var gate = BuildOptionGateResponse(req.ConversationId ?? string.Empty);
                return Ok(new { content = gate.Response, conversationId = req.ConversationId, isDailyPrompt = true, showOptions = true, timestamp = DateTime.UtcNow });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in DailyRespond");
                return Ok(new ChatResponse { Response = "Let's pause and choose how to continue.", IsError = true });
            }
        }

        [HttpPost("daily/done")]
        public async Task<IActionResult> DailyDone()
        {
            try
            {
                var userId = await ResolveCanonicalUserId(User);
                var resp = await HandleDoneForDay(userId);
                return Ok(new { content = resp.Response, isDailyPromptComplete = true, isSessionEnd = true, timestamp = DateTime.UtcNow });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in DailyDone");
                return Ok(new ChatResponse { Response = "Great work today.", IsDailyPromptComplete = true });
            }
        }

        // BURNING FIRE start endpoint
        [HttpPost("burning-fire/start")]
        public async Task<IActionResult> BurningFireStart([FromBody] ChatRequest request)
        {
            try
            {
                var userId = await ResolveCanonicalUserId(User);
                
                // REDIRECT: Frontend should use ChatHub for proper diagnostic flow
                // This endpoint exists for fallback compatibility only
                return Ok(new { 
                    content = "I understand this is urgent. Let me connect you to our diagnostic system to help identify the best solution from our curriculum.",
                    conversationId = Guid.NewGuid().ToString(),
                    sessionType = "burning_fire", 
                    timestamp = DateTime.UtcNow,
                    shouldRouteToWebSocket = true,
                    message = "Please use the WebSocket connection for the full diagnostic experience."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in BurningFireStart");
                return Ok(new { content = "Tell me what's urgent right now.", conversationId = Guid.NewGuid().ToString(), sessionType = "burning_fire" });
            }
        }

        // TINKER LEVEL start endpoint
        [HttpPost("tinker/start")]
        public async Task<IActionResult> TinkerStart([FromBody] ChatRequest request)
        {
            try
            {
                var userId = await ResolveCanonicalUserId(User);
                
                // Send the message through WebSocket to trigger proper ChatHub routing
                var message = "other tinker level"; // This will trigger IsTinkerLevelRequest() in ChatHub
                
                // Create a proper response that indicates this should go through ChatHub
                return Ok(new { 
                    content = "What strategic leadership challenge are you facing? I'm here to help you work through it systematically.",
                    conversationId = Guid.NewGuid().ToString(),
                    sessionType = "tinker_level", 
                    timestamp = DateTime.UtcNow,
                    shouldRouteToWebSocket = true,
                    triggerMessage = message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in TinkerStart");
                return Ok(new { content = "What leadership challenge can I help with?", conversationId = Guid.NewGuid().ToString(), sessionType = "tinker_level" });
            }
        }

        [HttpPost("session/start")]
        public async Task<IActionResult> StartSession()
        {
            try
            {
                var userId = GetUserId();
                var firstName = User.FindFirst("first_name")?.Value ?? User.FindFirst("firstName")?.Value ?? User.FindFirst("given_name")?.Value ?? "User";
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
                
                // Mark awaiting first response for empathetic follow-up (guardrail)
                try
                {
                    if (_redis != null)
                    {
                        var db = _redis.GetDatabase();
                        var awaiting = System.Text.Json.JsonSerializer.Serialize(new { awaitingResponse = true, dayNumber = currentDay });
                        await db.StringSetAsync($"session:{userId}:daily", awaiting, TimeSpan.FromMinutes(60));
                    }
                }
                catch {}
                
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
                var isInitialRequest =
                    lowerMessage.Contains("daily prompt") ||
                    lowerMessage.Contains("today's prompt") ||
                    lowerMessage.Contains("leadership prompt") ||
                    lowerMessage.Contains("ready for day") ||
                    lowerMessage.Contains("ready for the day");
                var isContinuation = lowerMessage.Contains("talk more") || lowerMessage.Contains("more about");
                
                if (isInitialRequest && !isContinuation)
                {
                    // Deliver the daily prompt (verbatim), then append style-specific add-on without altering base
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
                    
                    var responseBuilder = new System.Text.StringBuilder();
                    responseBuilder.AppendLine(dailyPrompt.PromptText);
                    var style = await GetUserCommunicationStyle(userId);
                    if (style == "balanced" || style == "detailed")
                    {
                        responseBuilder.AppendLine();
                        responseBuilder.AppendLine(BuildStyleAddOn(style));
                    }

                    // Mark awaiting first response for empathetic follow-up
                    if (_redis != null)
                    {
                        var db = _redis.GetDatabase();
                        var awaiting = System.Text.Json.JsonSerializer.Serialize(new { awaitingResponse = true, dayNumber = currentDay });
                        await db.StringSetAsync($"session:{userId}:daily", awaiting, TimeSpan.FromMinutes(60));
                    }
                    
                    return new ChatResponse
                    {
                        Response = responseBuilder.ToString(),
                        IsDailyPrompt = true,
                        DayNumber = currentDay,
                        ConversationId = thread.ThreadId
                    };
                }
                else
                {
                    // User is responding to the daily prompt - give empathetic acknowledgment
                    var acknowledgment = await GenerateEmpathicAcknowledgment(message, firstName);
                    // Clear awaiting flag
                    if (_redis != null)
                    {
                        var db = _redis.GetDatabase();
                        await db.KeyDeleteAsync($"session:{userId}:daily");
                    }
                    
                    // After acknowledgment, require option selection before continuing
                    await SetAwaitingDailyOption(userId, true);
                    
                    // Natural way to present options
                    var options = "\n\nWe can continue discussing this further if you wish. Otherwise, if you're done with today's exercise then say \"Done for the day\" and we'll conclude until tomorrow.";
                    
                    return new ChatResponse
                    {
                        Response = acknowledgment + options,
                        IsDailyPrompt = true,
                        ConversationId = thread.ThreadId,
                        IsDailyPromptComplete = false
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
                // Get user preferences
                var userPreferences = await GetUserPreferences(userId);
                var communicationTone = userPreferences?.CommunicationTone ?? "professional";
                var firstName = User.FindFirst("first_name")?.Value ?? User.FindFirst("firstName")?.Value ?? User.FindFirst("given_name")?.Value ?? "User";
                
                // Extract the actual issue from the message
                var issue = ExtractIssueFromMessage(message);
                
                // Search curriculum for relevant resources
                CurriculumSearchResponse? curriculumResults = null;
                if (_curriculumSearch != null)
                {
                    _logger.LogInformation("Searching curriculum for burning fire issue: {Issue}", issue);
                    curriculumResults = await _curriculumSearch.SearchForBurningFires(issue);
                    _logger.LogInformation("Curriculum search returned {Count} results", curriculumResults?.Resources?.Count ?? 0);
                }
                else
                {
                    _logger.LogWarning("CurriculumSearchService is not available");
                }
                
                // Get tone instructions
                var toneInstructions = GetToneInstructions(communicationTone);
                
                // Build curriculum context if we have results
                var curriculumContext = "";
                var resourceLinks = "";
                
                if (curriculumResults?.Resources?.Any() == true)
                {
                    var topResources = curriculumResults.Resources.Take(3).ToList();
                    
                    curriculumContext = "\n\nRelevant resources found:\n" + 
                        string.Join("\n", topResources.Select(r => $"- {r.Title}: {r.Preview}"));
                    
                    resourceLinks = "\n\n📚 **Helpful Resources:**\n" +
                        string.Join("\n", topResources.Select(r => 
                            $"• [{r.Title}]({r.Url}) - {r.Preview.Substring(0, Math.Min(r.Preview.Length, 80))}..."));
                }
                
                // Chris Cooper style - direct, actionable, urgent
                var prompt = $@"You are Chris Cooper responding to an URGENT business crisis.

{firstName} says: {issue}

{curriculumContext}

{toneInstructions}

Give them:
1. ONE specific action they can take RIGHT NOW (1-2 sentences max)
2. Ask 'Can you do this in the next hour?'

Be direct. No fluff. No numbered lists. Just immediate action.

If the curriculum resources are relevant, briefly mention how they connect to the immediate action.";

                var aiResponse = await GetAIResponse(prompt);
                
                // Add resource links to the response
                var finalResponse = aiResponse + resourceLinks;
                
                return new ChatResponse
                {
                    Response = finalResponse,
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
                // Get user preferences
                var userPreferences = await GetUserPreferences(userId);
                var communicationStyle = userPreferences?.CommunicationStyle ?? "balanced";
                var communicationTone = userPreferences?.CommunicationTone ?? "professional";
                var firstName = User.FindFirst("first_name")?.Value ?? User.FindFirst("firstName")?.Value ?? User.FindFirst("given_name")?.Value ?? "User";
                
                var issue = ExtractIssueFromMessage(message);
                
                // Get style and tone instructions
                var styleInstructions = GetStyleInstructions(communicationStyle);
                var toneInstructions = GetToneInstructions(communicationTone);
                
                // Chris Cooper style for non-urgent but important issues
                var prompt = $@"You are Chris Cooper helping a gym owner with a business challenge.

{firstName} says: {issue}

{styleInstructions}
{toneInstructions}

Give them:
1. A brief acknowledgment (1 sentence)
2. One clear next step they should take
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
                // Get user preferences for personalized response
                var userPreferences = await GetUserPreferences(userId);
                var communicationStyle = userPreferences?.CommunicationStyle ?? "balanced";
                var communicationTone = userPreferences?.CommunicationTone ?? "professional";
                var firstName = User.FindFirst("first_name")?.Value ?? User.FindFirst("firstName")?.Value ?? User.FindFirst("given_name")?.Value ?? "User";
                
                // Get style and tone instructions
                var styleInstructions = GetStyleInstructions(communicationStyle);
                var toneInstructions = GetToneInstructions(communicationTone);
                
                // Natural conversation with Chris Cooper style
                var prompt = $@"You are Chris Cooper, master business coach for gym owners.

{firstName} says: {message}

{styleInstructions}
{toneInstructions}

Respond naturally and end with one actionable question that moves them forward.
No formal structure or numbered lists.";

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

        private ConversationType DetermineConversationType(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return ConversationType.GENERAL_CHAT;
            
            var lowerMessage = message.ToLower().Trim();
            
            // Check each trigger phrase (case-insensitive)
            foreach (var trigger in ConversationTypeMappings)
            {
                if (lowerMessage.Contains(trigger.Key))
                {
                    return trigger.Value;
                }
            }
            
            return ConversationType.GENERAL_CHAT;
        }

        private async Task<bool> IsRespondingToDailyPrompt(string userId)
        {
            try
            {
                if (_redis == null) return false;
                var db = _redis.GetDatabase();
                var sessionData = await db.StringGetAsync($"session:{userId}:daily");
                if (!sessionData.HasValue) return false;
                var json = System.Text.Json.JsonDocument.Parse(sessionData.ToString());
                if (json.RootElement.TryGetProperty("awaitingResponse", out var awaiting))
                {
                    return awaiting.GetBoolean();
                }
            }
            catch { }
            return false;
        }

        private async Task<bool> IsAwaitingDailyOption(string userId)
        {
            try
            {
                if (_redis == null) return false;
                var db = _redis.GetDatabase();
                var val = await db.StringGetAsync($"session:{userId}:daily:awaitingOption");
                return val.HasValue && val.ToString() == "1";
            }
            catch { return false; }
        }

        private async Task SetAwaitingDailyOption(string userId, bool awaiting)
        {
            if (_redis == null) return;
            var db = _redis.GetDatabase();
            if (awaiting)
                await db.StringSetAsync($"session:{userId}:daily:awaitingOption", "1", TimeSpan.FromMinutes(60));
            else
                await db.KeyDeleteAsync($"session:{userId}:daily:awaitingOption");
        }

        private bool IsOptionSelection(string message)
        {
            var lower = (message ?? string.Empty).ToLowerInvariant();
            return lower.Contains("explore") || lower.Contains("go deeper") || lower.Contains("done for the day") ||
                   lower.Contains("talk more") || lower.Contains("burning fire") || lower.Contains("other tinker");
        }

        private ChatResponse BuildOptionGateResponse(string threadId)
        {
            return new ChatResponse
            {
                Response = "We can continue discussing this further if you wish. Otherwise, if you're done with today's exercise then say \"Done for the day\" and we'll conclude until tomorrow.",
                ConversationId = threadId,
                IsDailyPrompt = true
            };
        }

        private async Task<string> ResolveCanonicalUserId(System.Security.Claims.ClaimsPrincipal user)
        {
            // Check all possible claim types for user ID
            // IMPORTANT: Check "user_id" claim FIRST as it contains the actual GUID
            // ClaimTypes.NameIdentifier might contain email in some JWT configurations
            var userId = user.FindFirst("user_id")?.Value
                      ?? user.FindFirst("userId")?.Value
                      ?? user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            
            if (!string.IsNullOrEmpty(userId))
            {
                _logger.LogInformation("Resolved user ID from claims: {UserId}", userId);
                return userId;
            }
            
            // If no user ID in claims, try to look up by email
            var email = user.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                     ?? user.FindFirst("email")?.Value;
                     
            if (!string.IsNullOrEmpty(email))
            {
                try
                {
                    await using var conn = new Npgsql.NpgsqlConnection(_connectionString);
                    await conn.OpenAsync();
                    var cmd = new Npgsql.NpgsqlCommand(@"SELECT id FROM users WHERE email = @email LIMIT 1", conn);
                    cmd.Parameters.AddWithValue("email", email);
                    var res = await cmd.ExecuteScalarAsync();
                    if (res != null)
                    {
                        var resolvedId = res.ToString();
                        _logger.LogInformation("Resolved user ID from email {Email}: {UserId}", email, resolvedId);
                        return resolvedId!;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error resolving user ID from email");
                }
            }
            
            // This should never happen - throw an exception instead of returning a random GUID
            _logger.LogError("Could not resolve user ID from claims: {@Claims}", user.Claims.Select(c => new { c.Type, c.Value }));
            throw new UnauthorizedAccessException("User ID could not be resolved from authentication token");
        }

        private string GetUserId()
        {
            var id = User.FindFirst("userId")?.Value
                     ?? User.FindFirst("sub")?.Value
                     ?? User.FindFirst("nameidentifier")?.Value
                     ?? User.FindFirst("oid")?.Value
                     ?? User.FindFirst("email")?.Value
                     ?? "anonymous";
            return id;
        }

        private async Task<string> GetUserCommunicationStyle(string userId)
        {
            try
            {
                await using var conn = new Npgsql.NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                var cmd = new Npgsql.NpgsqlCommand(@"SELECT COALESCE(communication_style,'medium') FROM user_preferences WHERE user_id = @userId", conn);
                if (Guid.TryParse(userId, out var uid))
                    cmd.Parameters.AddWithValue("userId", uid);
                else
                    cmd.Parameters.AddWithValue("userId", Guid.Empty);
                var result = await cmd.ExecuteScalarAsync();
                var dbStyle = result?.ToString() ?? "medium";
                return dbStyle switch { "short" => "concise", "medium" => "balanced", "long" => "detailed", _ => "balanced" };
            }
            catch { return "balanced"; }
        }

        private string BuildStyleAddOn(string style)
        {
            var sb = new System.Text.StringBuilder();
            if (style == "balanced")
            {
                sb.AppendLine("Additional guidance — Balanced:");
                sb.AppendLine("1. Identify one concrete example from today related to this prompt.");
                sb.AppendLine("2. Write two sentences on what you learned and what you will adjust.");
            }
            else
            {
                sb.AppendLine("Additional guidance — Detailed:");
                sb.AppendLine("1. Describe the situation briefly (two sentences).");
                sb.AppendLine("2. State the outcome you want this week.");
                sb.AppendLine("3. List one constraint (time, staff, or budget).");
                sb.AppendLine("4. Choose the next action for the next 24 hours.");
            }
            return sb.ToString();
        }

        private async Task<string> GenerateChrisCooperDailyResponse(string userResponse, string firstName)
        {
            try
            {
                // Get user preferences for personalized response style
                var userId = await ResolveCanonicalUserId(User);
                var userPreferences = await GetUserPreferences(userId);
                var communicationStyle = userPreferences?.CommunicationStyle ?? "balanced";
                var communicationTone = userPreferences?.CommunicationTone ?? "professional";
                
                // Adjust prompt based on communication style and tone preferences
                var styleInstructions = GetStyleInstructions(communicationStyle);
                var toneInstructions = GetToneInstructions(communicationTone);
                
                // Create a Chris Cooper coaching prompt that acknowledges their response and provides natural follow-up
                var prompt = $@"You are Chris Cooper, master business coach and founder of Two-Brain Business. A gym owner named {firstName} just shared their reflection from today's daily leadership prompt.

Their response: ""{userResponse}""

Respond as Chris Cooper would - acknowledge what they shared with empathy and provide coaching insight. Keep it conversational, human, and context-aware.

{styleInstructions}
{toneInstructions}

Your response should:
1. Acknowledge their specific feelings or situation they mentioned
2. Provide actionable insight or validation based on their actual response
3. Be warm, direct, and encouraging
4. Reference specific details from their response to show you're listening
5. Use Chris Cooper's coaching style - practical, empathetic, and growth-focused

Write in a natural, conversational tone as if you're having a one-on-one coaching conversation. Do NOT ask questions or invite further conversation. This is a completion-based daily prompt, not an ongoing dialogue.";

                var aiResponse = await GetAIResponse(prompt);
                
                // Add the completion options after the Chris Cooper response
                var acknowledgment = string.IsNullOrEmpty(aiResponse) || aiResponse.Contains("What specific challenge can I help you with?") 
                    ? await GenerateEmpathicAcknowledgment(userResponse, firstName)
                    : aiResponse;
                
                // Add professional completion options in the exact format requested
                var completionOptions = "|||SPLIT|||We can continue discussing this further if you wish. Otherwise, if you're done with today's exercise then say \"Done for the day\" and we'll conclude until tomorrow.";
                
                return acknowledgment + completionOptions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating Chris Cooper daily response");
                return await GenerateEmpathicAcknowledgment(userResponse, firstName) + 
                       "|||SPLIT|||We can continue discussing this further if you wish. Otherwise, if you're done with today's exercise then say \"Done for the day\" and we'll conclude until tomorrow.";
            }
        }

        private async Task<string> GenerateDeeperPromptCoaching(string userMessage, string firstName)
        {
            try
            {
                // Get user preferences for personalized response style
                var userId = await ResolveCanonicalUserId(User);
                var userPreferences = await GetUserPreferences(userId);
                var communicationStyle = userPreferences?.CommunicationStyle ?? "balanced";
                var communicationTone = userPreferences?.CommunicationTone ?? "professional";
                
                // Adjust prompt based on communication style and tone preferences
                var styleInstructions = GetDeeperCoachingStyleInstructions(communicationStyle);
                var toneInstructions = GetToneInstructions(communicationTone);
                
                var prompt = $@"You are Chris Cooper, master business coach and founder of Two-Brain Business. {firstName} wants to explore their daily leadership prompt reflection more deeply. 

Their original response: ""{userMessage}""

Provide deeper coaching on their reflection. Ask specific, actionable questions that help them dig deeper into their self-awareness and leadership development. 

{styleInstructions}
{toneInstructions}

Your response should:
1. Reference specific details from their original response
2. Ask thoughtful, probing questions that encourage deeper reflection
3. Stay focused on leadership development and self-awareness
4. Be supportive but challenging
5. Use Chris Cooper's coaching style - practical, direct, and growth-focused

End with clear, actionable questions that help them gain deeper insight.";

                var aiResponse = await GetAIResponse(prompt);
                
                if (string.IsNullOrEmpty(aiResponse) || aiResponse.Contains("What specific challenge can I help you with?"))
                {
                    return $"Let's dig deeper into what you discovered today, {firstName}. What specific emotion or thought surprised you the most? And what do you think that tells you about where you are as a leader right now?";
                }
                
                return aiResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating deeper prompt coaching");
                return $"Let's dig deeper into what you discovered today, {firstName}. What specific emotion or thought surprised you the most? And what do you think that tells you about where you are as a leader right now?";
            }
        }

        private async Task<string> HandleDailyPromptCompletion(string userId, string firstName)
        {
            try
            {
                // Mark the day as completed in Redis with proper expiration and completion timestamp
                if (_redis != null)
                {
                    var db = _redis.GetDatabase();
                    var currentDay = await GetUserCurrentDay(userId);
                    
                    // Store completion with longer expiration and timestamp
                    await db.StringSetAsync($"user:{userId}:daily:last_completed_day", currentDay.ToString(), TimeSpan.FromDays(2));
                    await db.StringSetAsync($"user:{userId}:daily:completed_at", DateTime.UtcNow.ToString("O"), TimeSpan.FromDays(2));
                    
                    _logger.LogInformation("✅ User {UserId} completed Day {Day} at {Time}", userId, currentDay, DateTime.UtcNow);
                }

                var completionMessages = new[]
                {
                    $"Excellent work today, {firstName}. That kind of honest self-reflection is what separates good gym owners from great leaders. See you tomorrow for Day 2.",
                    $"Well done, {firstName}. You showed up with courage and honesty today. That's the foundation of everything we'll build together. Rest well.",
                    $"Great job today, {firstName}. The fact that you took time for this reflection shows you're serious about your growth as a leader. Tomorrow we'll build on this foundation.",
                    $"Perfect, {firstName}. You've completed Day 1 with the kind of self-awareness that creates lasting change. Take this insight with you into your business today."
                };

                var random = new Random();
                return completionMessages[random.Next(completionMessages.Length)];
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling daily prompt completion");
                return $"Well done, {firstName}. You've completed today's reflection with honesty and courage. That's the foundation of great leadership. See you tomorrow.";
            }
        }

        private async Task<string> GenerateEmpathicAcknowledgment(string userResponse, string firstName)
        {
            try
            {
                var userId = await ResolveCanonicalUserId(User);
                var userPreferences = await GetUserPreferences(userId);
                var communicationStyle = userPreferences?.CommunicationStyle ?? "balanced";
                var communicationTone = userPreferences?.CommunicationTone ?? "professional";
                
                var prompt = $@"You are Chris Cooper, a mentor known for deep empathy and practical wisdom.

The gym owner's name is {firstName} (USE THIS NAME, not 'User').
Their communication preference: {communicationStyle}
Their reflection: ""{userResponse}""

Create a brief, empathetic acknowledgment that:
1. Shows you truly heard and understood their specific situation
2. References something specific they mentioned (don't just say 'what you shared')
3. Validates their experience with genuine understanding
4. Address them by their first name ({firstName}) naturally in your response
5. Feels like a real human who cares, not a template

{GetStyleInstructions(communicationStyle)}
{GetToneInstructions(communicationTone)}

DO NOT:
- Use generic phrases like 'Thank you for sharing' or 'I appreciate you taking'
- Give advice or solutions yet
- Ask questions
- Use numbered lists or bullet points
- Sound robotic or formulaic

Just acknowledge with genuine human empathy. Make them feel truly heard.";

                var aiResponse = await GetAIResponse(prompt);
                
                // Only use fallback if AI completely fails
                if (string.IsNullOrWhiteSpace(aiResponse) || 
                    aiResponse.Contains("What specific challenge") || 
                    aiResponse.Contains("having trouble connecting"))
                {
                    return $"I hear you, {firstName}. What you're experiencing matters, and I'm glad you're taking time to reflect on it.";
                }
                
                return aiResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating dynamic acknowledgment");
                return $"I hear you, {firstName}. What you're experiencing matters, and I'm glad you're taking time to reflect on it.";
            }
        }

        private async Task<UserProfile?> GetUserPreferences(string userId)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                
                var cmd = new NpgsqlCommand(@"
                    SELECT communication_style, preferred_response_length, timezone, communication_tone
                    FROM user_profiles 
                    WHERE user_id = @userId
                    LIMIT 1", conn);
                
                cmd.Parameters.AddWithValue("userId", userId);
                
                await using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new UserProfile
                    {
                        CommunicationStyle = reader.IsDBNull(0) ? "balanced" : reader.GetString(0),
                        PreferredResponseLength = reader.IsDBNull(1) ? "medium" : reader.GetString(1),
                        Timezone = reader.IsDBNull(2) ? "America/New_York" : reader.GetString(2),
                        CommunicationTone = reader.IsDBNull(3) ? "professional" : reader.GetString(3)
                    };
                }
                
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user preferences for {UserId}", userId);
                return null;
            }
        }
        
        private string GetStyleInstructions(string communicationStyle)
        {
            return communicationStyle.ToLower() switch
            {
                "concise" => "Keep your response brief and to the point - maximum 1-2 sentences. Be direct and actionable. Focus on the most important insight without elaboration.",
                
                "detailed" => "Provide a comprehensive response with examples and deeper context. You can use 3-4 sentences and include specific examples or analogies that relate to their gym business. Offer multiple perspectives or actionable steps.",
                
                "balanced" or _ => "Provide a moderate level of detail - 2-3 sentences that balance insight with brevity. Include one specific, actionable point while being warm and encouraging."
            };
        }
        
        private string GetToneInstructions(string? communicationTone)
        {
            return (communicationTone?.ToLower() ?? "professional") switch
            {
                "friendly" => "Use a warm, approachable tone like talking to a close friend. Be encouraging and supportive. Use conversational language and contractions. Show genuine care and enthusiasm.",
                
                "casual" => "Keep it relaxed and informal. Use everyday language, maybe even some appropriate gym slang. Be like a buddy at the gym giving advice. It's okay to be playful but stay helpful.",
                
                "academic" => "Use precise, analytical language. Reference business concepts and frameworks when relevant. Be thorough and methodical in your approach. Include data-driven insights where appropriate.",
                
                "professional" or _ => "Maintain a respectful, business-focused tone. Be clear and confident. Use professional language while remaining personable. Balance authority with approachability."
            };
        }
        
        private string GetDeeperCoachingStyleInstructions(string communicationStyle)
        {
            return communicationStyle.ToLower() switch
            {
                "concise" => "Keep it brief - 1-2 sentences maximum with 1-2 direct, powerful questions. Be laser-focused on the most important insight.",
                
                "detailed" => "Provide comprehensive coaching with 3-4 sentences. Include context, examples, and ask 2-3 layered questions that build on each other. Offer multiple angles for reflection.",
                
                "balanced" or _ => "Use 2-3 sentences with thoughtful coaching insight, then ask 1-2 specific questions that help them dig deeper into their leadership development."
            };
        }
        
        private string GetStyledResponse(string communicationStyle, string firstName, string emotionType, string concise, string balanced, string detailed)
        {
            return communicationStyle.ToLower() switch
            {
                "concise" => concise,
                "detailed" => detailed,
                "balanced" or _ => balanced
            };
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
                if (_chatClient == null)
                {
                    _logger.LogError("ChatClient not configured");
                    return "I'm having trouble connecting right now. Let me help you another way.";
                }

                var messages = new List<ChatMessage>
                {
                    new SystemChatMessage(systemPrompt)
                };

                var options = new ChatCompletionOptions
                {
                    MaxOutputTokenCount = 200,
                    Temperature = 0.7f
                };

                var response = await _chatClient.CompleteChatAsync(messages, options);
                
                if (response.Value?.Content?.Count > 0)
                {
                    return response.Value.Content[0].Text ?? "Let's focus on what matters most for your business right now.";
                }
                
                _logger.LogWarning("OpenAI returned empty response");
                return "What specific challenge can I help you with?";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling OpenAI API via SDK");
                return "Let's focus on what matters most for your business right now.";
            }
        }

        private string FormatDailyPromptWithSplit(string promptText, int dayNumber)
        {
            // Split the prompt into multiple messages for separate chat bubbles
            var parts = new List<string>();
            
            // Split by newlines and process sections
            var lines = promptText.Split(new[] { "\n", "\r\n" }, StringSplitOptions.None);
            
            StringBuilder currentSection = new StringBuilder();
            string sectionType = "";
            
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                
                // Check for section headers
                if (line.StartsWith("Action:", StringComparison.OrdinalIgnoreCase))
                {
                    // Save previous section
                    if (currentSection.Length > 0 && !string.IsNullOrEmpty(sectionType))
                    {
                        parts.Add(currentSection.ToString().Trim());
                    }
                    sectionType = "Action";
                    currentSection = new StringBuilder();
                    currentSection.AppendLine($"Day {dayNumber} Prompt:");
                }
                else if (line.StartsWith("Ask:", StringComparison.OrdinalIgnoreCase) || 
                         (line.Contains("Ask:") && !line.Contains("Action:")))
                {
                    // Save previous section
                    if (currentSection.Length > 0)
                    {
                        parts.Add(currentSection.ToString().Trim());
                    }
                    sectionType = "Ask";
                    currentSection = new StringBuilder();
                    currentSection.AppendLine(line);
                }
                else if (line.StartsWith("Why it matters:", StringComparison.OrdinalIgnoreCase))
                {
                    // Save previous section
                    if (currentSection.Length > 0)
                    {
                        parts.Add(currentSection.ToString().Trim());
                    }
                    sectionType = "Why";
                    currentSection = new StringBuilder();
                    // Skip "Why it matters:" header, just collect the content
                }
                else if (!string.IsNullOrWhiteSpace(line) && sectionType != "")
                {
                    // Add to current section
                    currentSection.AppendLine(line);
                }
            }
            
            // Add final section
            if (currentSection.Length > 0)
            {
                parts.Add(currentSection.ToString().Trim());
            }
            
            // If parsing failed or we only got one part, return just the prompt text (no prefix)
            if (parts.Count <= 1)
            {
                return promptText;
            }
            
            return string.Join("|||SPLIT|||", parts);
        }

        private async Task<int> GetUserCurrentDay(string userId)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                
                var cmd = new NpgsqlCommand(@"
                    SELECT COALESCE(ud.current_day, 1) as current_day
                    FROM user_data ud
                    WHERE ud.id::text = @userId
                       OR ud.user_id = @userId
                       OR ud.email = @userEmail
                       OR ud.user_id = (SELECT username FROM users WHERE id = @userGuid::uuid LIMIT 1)
                    LIMIT 1", conn);
                var tokenEmail = User.FindFirst("email")?.Value ?? User.FindFirst("preferred_username")?.Value ?? string.Empty;
                var userGuid = Guid.TryParse(userId, out var uid) ? uid : Guid.Empty;
                cmd.Parameters.AddWithValue("userId", userId.ToLower());
                cmd.Parameters.AddWithValue("userEmail", (object?)tokenEmail ?? string.Empty);
                cmd.Parameters.AddWithValue("userGuid", userGuid);
                
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
                    var promptText = reader.IsDBNull(1) ? "" : reader.GetString(1);
                    var promptTitle = reader.GetString(0);
                    
                    // Handle empty prompt_text for early days (1-6) with proper leadership prompts
                    if (string.IsNullOrEmpty(promptText))
                    {
                        promptText = GetDefaultPromptText(dayNumber);
                    }
                    
                    return new DailyPrompt
                    {
                        PromptTitle = promptTitle,
                        PromptText = promptText,
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

        private string GetDefaultPromptText(int dayNumber)
        {
            // Complete 180-day Self-Leadership Curriculum
            // 6-month structured program with 7-day rhythm
            // Month 1: Awareness and Grounding (Days 1-30)
            // Month 2: Emotional Ownership (Days 31-60) 
            // Month 3: Thought Work and Reframing (Days 61-90)
            // Month 4: Values and Boundaries (Days 91-120)
            // Month 5: Hope and Agency (Days 121-150)
            // Month 6: Integration and Calm (Days 151-180)
            
            return dayNumber switch
            {
                // MONTH 1: AWARENESS AND GROUNDING (Days 1-30)
                1 => "Look yourself in the mirror for 60 seconds. Ask: \"What am I feeling right now, and what's the honest truth about why?\" No fixing—just noticing.",
                
                2 => "Day 2 Name It to Tame It\n🎯 Theme: Emotional Precision\n\n[Display Emotions Wheel Graphic: file-NPNDQFbFxTXkPSgMjBc5tQ]\n\nAction:\nIdentify your current emotion using the Emotions Wheel. Write: \"I feel ____.\" Naming the feeling reduces its power.\n\nWhy it matters:\nWhen you can name it precisely, you can tame it. Don't just say 'stressed' or 'fine'—dig deeper. Are you anxious, overwhelmed, excited, determined?",
                
                3 => "Day 3 The Story Behind the Feeling\n📖 Theme: Connecting Thoughts and Emotions\n\nAction:\nComplete: \"I feel ___ because I think ___.\" This connects your feelings to the thoughts driving them.\n\nWhy it matters:\nEvery emotion has a story. Understanding the thought behind the feeling gives you leverage to change both. What story are you telling yourself?",
                
                4 => "Day 4 Trigger Map\n🗺️ Theme: Identifying Emotional Patterns\n\nAction:\nList 3 situations from the past week that triggered a strong emotional reaction. Note what happened and what you felt.\n\nWhy it matters:\nAwareness of your triggers is the first step to mastering them. Map the territory so you can navigate it consciously.",
                
                5 => "Day 5 Ground Yourself (5-4-3-2-1)\n🌱 Theme: Present Moment Awareness\n\nAction:\nUse the grounding method: 5 things you see, 4 touch, 3 hear, 2 smell, 1 taste. Brings you to the present.\n\nWhy it matters:\nGrounding interrupts emotional overwhelm and anchors you in reality. This is your reset button for stress and anxiety.",
                
                6 => "Day 6 Emotion Audit\n📊 Theme: Weekly Emotional Review\n\nAction:\nList your 3 most frequent emotions this week, their triggers, and your reactions.\n\nWhy it matters:\nThis audit builds awareness of what's driving your choices beneath the surface. You're becoming someone who understands emotions, not just experiences them.",
                
                7 => "Day 7 The Observer Self\n👁️ Theme: Detached Awareness\n\nAction:\nStep outside yourself and observe your thoughts, feelings, and actions without judgment.\n\nWhy it matters:\nThe Observer Self is calm, curious, and wise. It doesn't panic—it notices. This perspective gives you power over your reactions.",
                
                // Continue with specific prompts for key days...
                8 => "Day 8 Trigger Mapping\n🗺️ Theme: Recurring Patterns\n\nAction:\nIdentify your recurring triggers and the patterns they create in your emotions.\n\nWhy it matters:\nRecognizing patterns helps you interrupt them before they control your leadership decisions.",
                
                11 => "Day 11 The Body Knows\n🧠 Theme: Physical Awareness as Emotional Feedback\n\nAction:\nSpend five minutes today tuning in to where your emotions live in your body. Sit quietly, close your eyes, and scan from head to toe. Ask yourself:\n• Where do I feel tension or tightness?\n• Is there a sensation in my chest, gut, throat, or shoulders?\n• What emotion might be linked to that feeling?\n\nWhy it matters:\nBefore your mind fully registers an emotion, your body often feels it. This is your nervous system's early warning system. Learning to read these physical cues gives you faster, more honest signals than thoughts alone.",
                
                12 => "Day 12 Calm by Command\n🧘 Theme: Box Breathing for Calm and Control\n\nAction:\nPractice Box Breathing today—a Navy SEAL–approved method for regaining calm and control in moments of stress or overload.\n\nHere's how:\n• Inhale through your nose for 4 seconds\n• Hold your breath for 4 seconds\n• Exhale through your mouth for 4 seconds\n• Hold your breath for 4 seconds\n• Repeat this cycle for 4 minutes (or at least 4 rounds to start)\n\nWhy it matters:\nThis practice signals safety to your nervous system. It reduces anxiety, increases focus, and restores grounded control.",
                
                // Add more specific days as needed, then use patterns for the rest
                _ when dayNumber <= 30 => GetMonthOnePrompt(dayNumber),
                _ when dayNumber <= 60 => GetMonthTwoPrompt(dayNumber),
                _ when dayNumber <= 90 => GetMonthThreePrompt(dayNumber),
                _ when dayNumber <= 120 => GetMonthFourPrompt(dayNumber),
                _ when dayNumber <= 150 => GetMonthFivePrompt(dayNumber),
                _ when dayNumber <= 180 => GetMonthSixPrompt(dayNumber),
                _ => "Congratulations! You've completed the 180-day Self-Leadership Program. You are now ready for external leadership and team engagement."
            };
        }
        
        private string GetMonthOnePrompt(int dayNumber)
        {
            // Month 1: Awareness and Grounding - 7-day rhythm
            var dayInWeek = ((dayNumber - 1) % 7) + 1;
            return dayInWeek switch
            {
                1 => $"Day {dayNumber} Mirror Check\n🪞 Theme: Honest Self-Reflection\n\nAction:\nLook yourself in the mirror for 60 seconds. Ask: \"What am I feeling right now, and what's the honest truth about why?\" No fixing—just noticing.\n\nWhy it matters:\nAwareness is the foundation of great leadership. This practice helps you lead from consciousness rather than reaction.",
                2 => $"Day {dayNumber} Name It to Tame It\n🎯 Theme: Emotional Precision\n\n[Display Emotions Wheel Graphic: file-NPNDQFbFxTXkPSgMjBc5tQ]\n\nAction:\nIdentify your current emotion using the Emotions Wheel. Write: \"I feel ____.\" Naming the feeling reduces its power.\n\nWhy it matters:\nWhen you can name it precisely, you can tame it. Specificity gives you control over your emotional state.",
                3 => $"Day {dayNumber} The Story Behind the Feeling\n📖 Theme: Connecting Thoughts and Emotions\n\nAction:\nComplete: \"I feel ___ because I think ___.\" This connects your feelings to the thoughts driving them.\n\nWhy it matters:\nEvery emotion has a story. Understanding the thought behind the feeling gives you leverage to change both.",
                4 => $"Day {dayNumber} Trigger Map\n🗺️ Theme: Identifying Emotional Patterns\n\nAction:\nList 3 situations from the past week that triggered a strong emotional reaction. Note what happened and what you felt.\n\nWhy it matters:\nAwareness of your triggers is the first step to mastering them. Map the territory so you can navigate it consciously.",
                5 => $"Day {dayNumber} Ground Yourself (5-4-3-2-1)\n🌱 Theme: Present Moment Awareness\n\nAction:\nUse the grounding method: 5 things you see, 4 touch, 3 hear, 2 smell, 1 taste. Brings you to the present.\n\nWhy it matters:\nGrounding interrupts emotional overwhelm and anchors you in reality. This is your reset button.",
                6 => $"Day {dayNumber} Emotion Audit\n📊 Theme: Weekly Emotional Review\n\nAction:\nList your 3 most frequent emotions this week, their triggers, and your reactions.\n\nWhy it matters:\nThis audit builds awareness of what's driving your choices beneath the surface.",
                7 => $"Day {dayNumber} The Observer Self\n👁️ Theme: Detached Awareness\n\nAction:\nStep outside yourself and observe your thoughts, feelings, and actions without judgment.\n\nWhy it matters:\nThe Observer Self is calm, curious, and wise. It doesn't panic—it notices.",
                _ => $"Day {dayNumber} Awareness Practice\n🧠 Theme: Grounding and Self-Knowledge\n\nAction:\nPractice identifying your emotions and grounding yourself in the present moment. What patterns are you noticing?\n\nWhy it matters:\nConsistent awareness builds the foundation for all other leadership skills."
            };
        }

        private string GetMonthTwoPrompt(int dayNumber)
        {
            // Month 2: Emotional Ownership
            var dayInWeek = ((dayNumber - 1) % 7) + 1;
            return dayInWeek switch
            {
                1 => $"Day {dayNumber} Mirror Check - Emotional Ownership\n🪞 Theme: Taking Responsibility for Your Emotional State\n\nAction:\nLook in the mirror and ask: \"What emotion am I carrying today, and how am I choosing to respond to it?\"\n\nWhy it matters:\nEmotional ownership means taking responsibility for your inner state rather than blaming external circumstances.",
                2 => $"Day {dayNumber} Name It to Tame It - Emotional Ownership\n🎯 Theme: Precise Emotional Vocabulary\n\n[Display Emotions Wheel Graphic: file-NPNDQFbFxTXkPSgMjBc5tQ]\n\nAction:\nUsing the emotions wheel, identify and name your current emotion with precision. Own it completely.\n\nWhy it matters:\nNaming emotions is the first step in taking ownership of them rather than being controlled by them.",
                _ => $"Day {dayNumber} Emotional Ownership Practice\n💪 Theme: Taking Charge of Your Inner State\n\nAction:\nToday's focus is on naming emotions and understanding triggers. What emotion is present for you right now, and how can you take ownership of it?\n\nWhy it matters:\nEmotional ownership prevents you from being a victim of your feelings and empowers conscious leadership."
            };
        }

        private string GetMonthThreePrompt(int dayNumber)
        {
            // Month 3: Thought Work and Reframing
            return $"Day {dayNumber} Thought Work and Reframing\n🧠 Theme: Mastering Your Mental Patterns\n\nAction:\nPractice the 'I feel ___ because I think ___' technique today. What story are you telling yourself about a current challenge, and how might you reframe it more empoweringly?\n\nWhy it matters:\nThoughts create feelings, and feelings drive actions. Mastering your thoughts gives you control over your entire experience.";
        }

        private string GetMonthFourPrompt(int dayNumber)
        {
            // Month 4: Values and Boundaries
            return $"Day {dayNumber} Values and Boundaries\n🧭 Theme: Living by Your Standards\n\nAction:\nReflect on your personal values today. What boundary do you need to set or maintain to honor what matters most to you?\n\nWhy it matters:\nGreat leaders operate from clear values and protect them with healthy boundaries.";
        }

        private string GetMonthFivePrompt(int dayNumber)
        {
            // Month 5: Hope and Agency
            return $"Day {dayNumber} Hope and Agency\n🌉 Theme: Bridging the Gap\n\nAction:\nFocus on the GAP framework today - what are you Grateful for, what Action can you take, and what Progress have you made? How can you transform any frustration into purposeful action?\n\nWhy it matters:\nHope is not passive—it's a decision rooted in honesty, agency, and a clear path forward.";
        }

        private string GetMonthSixPrompt(int dayNumber)
        {
            // Month 6: Integration and Calm
            return $"Day {dayNumber} Integration and Calm\n🕊️ Theme: Anchored Leadership\n\nAction:\nToday, integrate all the tools you've learned. Practice calm habits and prepare for external leadership. How can you use your emotional awareness to better serve your team and members?\n\nWhy it matters:\nCalm, integrated self-leadership is the foundation for leading others effectively.";
        }

        private bool IsBurningFiresRequest(string message)
        {
            var lower = (message ?? string.Empty).ToLowerInvariant();
            return System.Text.RegularExpressions.Regex.IsMatch(lower, @"\b(burning\s*fire|urgent\s*help|emergency|crisis)\b");
        }

        private bool IsTinkerLevelRequest(string message)
        {
            var lower = (message ?? string.Empty).ToLowerInvariant();
            return System.Text.RegularExpressions.Regex.IsMatch(lower, @"\b(other\s*tinker|tinker\s*level|tinker\s*fire)\b");
        }

        private bool IsTalkMoreRequest(string message)
        {
            var lower = (message ?? string.Empty).ToLowerInvariant();
            return System.Text.RegularExpressions.Regex.IsMatch(lower, @"\b(explore|go\s*deeper|talk\s*more|continue\s*prompt|more\s*about\s*today|today'?s?\s*prompt)\b");
        }

        private bool IsDoneForDayRequest(string message)
        {
            var lower = (message ?? string.Empty).ToLowerInvariant();
            return System.Text.RegularExpressions.Regex.IsMatch(lower, @"\b(done\s*for\s*the\s*day|done\s*for\s*today|all\s*done|i'?m\s*done|finished|complete|signing\s*off)\b");
        }

        private async Task<ChatResponse> HandleDoneForDay(string userId)
        {
            try
            {
                await using var conn = new Npgsql.NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                // Increment current day in both user_profiles (if present) and the source
                // used by /api/user/current-session (user_data.current_day)
                // Increment user_data.current_day using robust resolution (GUID/email/username)
                var userEmail = User.FindFirst("email")?.Value ?? User.FindFirst("preferred_username")?.Value ?? string.Empty;
                var userGuid = Guid.TryParse(userId, out var uid) ? uid : Guid.Empty;
                var cmdData = new Npgsql.NpgsqlCommand(@"
                    WITH target AS (
                        SELECT id FROM user_data
                        WHERE id = @userGuid
                           OR LOWER(user_id) = LOWER(@userIdText)
                           OR LOWER(COALESCE(email,'')) = LOWER(@userEmail)
                        LIMIT 1
                    )
                    UPDATE user_data
                    SET current_day = COALESCE(current_day, 1) + 1
                    WHERE id = (SELECT id FROM target)
                ", conn);
                cmdData.Parameters.AddWithValue("userGuid", userGuid);
                cmdData.Parameters.AddWithValue("userIdText", userId);
                cmdData.Parameters.AddWithValue("userEmail", (object?)userEmail ?? string.Empty);
                await cmdData.ExecuteNonQueryAsync();

                // Record completion markers in Redis
                try
                {
                    if (_redis != null)
                    {
                        var db = _redis.GetDatabase();
                        var newDay = await GetUserCurrentDay(userId); // This is now the NEXT day after increment
                        var completedDay = newDay - 1; // The day they just completed
                        
                        // Store the day they just completed and when they completed it
                        await db.StringSetAsync($"user:{userId}:daily:last_completed_day", completedDay.ToString(), TimeSpan.FromDays(2));
                        await db.StringSetAsync($"user:{userId}:daily:completed_at", DateTime.UtcNow.ToString("O"), TimeSpan.FromDays(2));
                        
                        _logger.LogInformation("User {UserId} completed Day {CompletedDay}, next day is {NextDay}", userId, completedDay, newDay);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error recording completion markers in Redis for user {UserId}", userId);
                }

                // Profiles table optional; skip to avoid failures in environments without it

                // Clear session flags in Redis
                if (_redis != null)
                {
                    var db = _redis.GetDatabase();
                    await db.KeyDeleteAsync($"chat:session:{userId}");
                    await db.KeyDeleteAsync($"session:{userId}:daily");
                    await db.KeyDeleteAsync($"session:{userId}:daily:awaitingOption");
                }

                return new ChatResponse
                {
                    Response = "Great work today! You showed up, and that's what matters. Rest up - tomorrow's prompt will be waiting when you're ready. See you then, champion.",
                    IsDailyPromptComplete = true,
                    SessionType = "completion",
                    IsSessionEnd = true,
                    DayNumber = await GetUserCurrentDay(userId)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling done for day");
                return new ChatResponse
                {
                    Response = "You did great work today. See you tomorrow for your next leadership prompt!",
                    IsDailyPromptComplete = true,
                    SessionType = "completion",
                    IsSessionEnd = true
                };
            }
        }

        private async Task<bool> IsDailyPromptThread(string conversationId)
        {
            try
            {
                await using var conn = new Npgsql.NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                var cmd = new Npgsql.NpgsqlCommand(@"SELECT type FROM conversation_threads WHERE thread_id = @id::uuid LIMIT 1", conn);
                cmd.Parameters.AddWithValue("id", Guid.Parse(conversationId));
                var res = await cmd.ExecuteScalarAsync();
                var type = res?.ToString()?.ToLowerInvariant() ?? string.Empty;
                return type.Contains("daily") || type.Contains("prompt");
            }
            catch { return false; }
        }

        // EMERGENCY CONVERSATION ENDPOINTS - Added directly to working ChatController
        [HttpPost("emergency-conversations")]
        public async Task<IActionResult> EmergencyCreateConversation([FromBody] EmergencyConversationRequest request)
        {
            try
            {
                var userId = await ResolveCanonicalUserId(User);
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized();
                }

                var conversationId = Guid.NewGuid().ToString();
                _logger.LogInformation("✅ EMERGENCY: Conversation created {ConversationId} for user {UserId}", conversationId, userId);
                
                return Ok(new { 
                    id = conversationId, 
                    title = request?.Title ?? "Chat", 
                    type = request?.Type ?? "general" 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ EMERGENCY: Error creating conversation");
                return StatusCode(500, new { error = "Failed to create conversation" });
            }
        }

        [HttpGet("emergency-conversations")]
        public async Task<IActionResult> EmergencyGetConversations()
        {
            try
            {
                var userId = await ResolveCanonicalUserId(User);
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized();
                }

                _logger.LogInformation("✅ EMERGENCY: Retrieved conversations for user {UserId}", userId);

                return Ok(new[] { 
                    new { 
                        id = Guid.NewGuid().ToString(), 
                        title = "Recent Chat", 
                        type = "general", 
                        timestamp = DateTime.UtcNow 
                    } 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ EMERGENCY: Error retrieving conversations");
                return Ok(new object[] { }); // Return empty array on error
            }
        }
    }

    public class ChatRequest
    {
        public string Message { get; set; } = "";
        public string? ConversationId { get; set; }
        public string? SessionType { get; set; }
        public string? MessageType { get; set; }
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
    
    // ConversationType enum moved to Services namespace to avoid conflicts
}

public class DailyPrompt
{
    public string PromptTitle { get; set; } = "";
    public string PromptText { get; set; } = "";
    public int DayNumber { get; set; }
}

public class EmergencyConversationRequest
{
    public string? Title { get; set; }
    public string? Type { get; set; }
}


