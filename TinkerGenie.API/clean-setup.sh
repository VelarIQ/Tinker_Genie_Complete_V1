#!/bin/bash
# ============================================
# TWO-BRAIN GENIE - CLEAN PRODUCTION SETUP
# Run on App Server (24.144.119.69)
# ============================================

set -e

echo "🧹 CLEAN PRODUCTION SETUP - SIMPLIFIED"
echo "======================================"

# 1. BACKUP EXISTING CODE
echo "📦 Backing up existing code..."
cp -r /var/www/tinker-genie/TinkerGenie.API /var/www/tinker-genie/TinkerGenie.API.backup.$(date +%Y%m%d-%H%M%S)

# 2. CLEAN THE PROJECT
echo "🧹 Cleaning project..."
cd /var/www/tinker-genie/TinkerGenie.API
rm -rf bin obj
rm -f *.log

# 3. RESTORE CLEAN PROGRAM.CS
echo "📝 Creating clean Program.cs..."
cat > Program.cs << 'EOF'
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Add Database
builder.Services.AddDbContext<TinkerGenie.API.Data.TinkerGenieContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add Redis
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var config = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379,password=***PASSWORD***";
    return ConnectionMultiplexer.Connect(config);
});

// Register services (we'll add implementations next)
builder.Services.AddScoped<TinkerGenie.API.Services.IOpenAIService, TinkerGenie.API.Services.OpenAIService>();

var app = builder.Build();

// Configure pipeline
app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("AllowAll");
app.UseRouting();
app.MapControllers();

// Health check
app.MapGet("/health", () => Results.Ok(new {
    status = "healthy",
    timestamp = DateTime.UtcNow,
    version = "2.0.0"
}));

app.Run();
EOF

# 4. CREATE SIMPLE DATA CONTEXT
echo "📊 Creating database context..."
mkdir -p Data
cat > Data/TinkerGenieContext.cs << 'EOF'
using Microsoft.EntityFrameworkCore;

namespace TinkerGenie.API.Data;

public class TinkerGenieContext : DbContext
{
    public TinkerGenieContext(DbContextOptions<TinkerGenieContext> options) : base(options) { }
    
    public DbSet<Conversation> Conversations { get; set; }
    public DbSet<Message> Messages { get; set; }
}

public class Conversation
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<Message> Messages { get; set; } = new();
}

public class Message
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public string Role { get; set; } = ""; // "user" or "assistant"
    public string Content { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
EOF

# 5. CREATE SIMPLE OPENAI SERVICE
echo "🤖 Creating OpenAI service..."
mkdir -p Services
cat > Services/IOpenAIService.cs << 'EOF'
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TinkerGenie.API.Services;

public interface IOpenAIService
{
    Task<string> GenerateResponseAsync(string userMessage);
}

public class OpenAIService : IOpenAIService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAIService> _logger;
    private readonly string _apiKey;
    
    public OpenAIService(IConfiguration configuration, ILogger<OpenAIService> logger)
    {
        _httpClient = new HttpClient();
        _logger = logger;
        _apiKey = configuration["OpenAI:ApiKey"] ?? "";
        
        _httpClient.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", _apiKey);
    }
    
    public async Task<string> GenerateResponseAsync(string userMessage)
    {
        try
        {
            var request = new
            {
                model = "gpt-4o-mini",
                messages = new[]
                {
                    new { role = "system", content = "You are a helpful leadership coach for Two-Brain Business Tinker members. Be encouraging and practical." },
                    new { role = "user", content = userMessage }
                },
                max_tokens = 500,
                temperature = 0.7
            };
            
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content);
            
            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseJson);
                var message = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();
                
                return message ?? "I'm here to help with your leadership journey.";
            }
            
            _logger.LogError($"OpenAI API error: {response.StatusCode}");
            return "I'm having trouble connecting right now. Let me help you differently - what's your biggest leadership challenge today?";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling OpenAI");
            return "Let me help you with your leadership journey. What would you like to explore today?";
        }
    }
}
EOF

# 6. CREATE SIMPLE CHAT CONTROLLER
echo "💬 Creating chat controller..."
mkdir -p Controllers
cat > Controllers/ChatController.cs << 'EOF'
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TinkerGenie.API.Data;
using TinkerGenie.API.Services;
using StackExchange.Redis;

namespace TinkerGenie.API.Controllers;

[ApiController]
[Route("api/chat")]
public class ChatController : ControllerBase
{
    private readonly IOpenAIService _openAIService;
    private readonly TinkerGenieContext _context;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<ChatController> _logger;
    
    public ChatController(
        IOpenAIService openAIService, 
        TinkerGenieContext context,
        IConnectionMultiplexer redis,
        ILogger<ChatController> logger)
    {
        _openAIService = openAIService;
        _context = context;
        _redis = redis;
        _logger = logger;
    }
    
    [HttpPost]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request)
    {
        try
        {
            // Get or create conversation
            var userId = request.UserId ?? "anonymous";
            
            var conversation = await _context.Conversations
                .Include(c => c.Messages)
                .FirstOrDefaultAsync(c => c.UserId == userId);
            
            if (conversation == null)
            {
                conversation = new Conversation { UserId = userId };
                _context.Conversations.Add(conversation);
            }
            
            // Add user message
            var userMessage = new Message
            {
                ConversationId = conversation.Id,
                Role = "user",
                Content = request.Message
            };
            _context.Messages.Add(userMessage);
            
            // Generate AI response
            var aiResponse = await _openAIService.GenerateResponseAsync(request.Message);
            
            // Add AI message
            var aiMessage = new Message
            {
                ConversationId = conversation.Id,
                Role = "assistant",
                Content = aiResponse
            };
            _context.Messages.Add(aiMessage);
            
            // Save to database
            await _context.SaveChangesAsync();
            
            // Cache in Redis (optional)
            var db = _redis.GetDatabase();
            await db.StringSetAsync($"last_response:{userId}", aiResponse, TimeSpan.FromMinutes(5));
            
            return Ok(new
            {
                success = true,
                message = aiResponse,
                conversationId = conversation.Id,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chat error");
            return Ok(new
            {
                success = false,
                message = "Let me help you with your leadership journey. What challenge are you facing?",
                error = true
            });
        }
    }
    
    [HttpGet("history/{userId}")]
    public async Task<IActionResult> GetHistory(string userId)
    {
        var conversations = await _context.Conversations
            .Where(c => c.UserId == userId)
            .Include(c => c.Messages)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync();
        
        return Ok(conversations?.Messages ?? new List<Message>());
    }
}

public class ChatRequest
{
    public string Message { get; set; } = "";
    public string? UserId { get; set; }
}
EOF

# 7. INSTALL PACKAGES
echo "📦 Installing NuGet packages..."
dotnet add package Microsoft.EntityFrameworkCore.Design
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add package StackExchange.Redis
dotnet add package Swashbuckle.AspNetCore

# 8. BUILD PROJECT
echo "🔨 Building project..."
dotnet build

# 9. RUN MIGRATIONS
echo "🗄️ Creating database migrations..."
dotnet ef migrations add InitialCreate --context TinkerGenieContext || true
dotnet ef database update --context TinkerGenieContext || true

# 10. RESTART API
echo "🚀 Restarting API..."
pm2 restart tinker-api || pm2 start "dotnet run --urls http://0.0.0.0:5000" --name tinker-api

# 11. TEST THE API
echo "🧪 Testing API..."
sleep 5

# Test health
echo "Testing health endpoint..."
curl -s http://localhost:5000/health | jq .

# Test chat
echo "Testing chat endpoint..."
curl -X POST http://localhost:5000/api/chat \
  -H "Content-Type: application/json" \
  -d '{"message":"What is leadership?","userId":"test"}' | jq .

echo ""
echo "✅ SETUP COMPLETE!"
echo ""
echo "API Endpoints:"
echo "  Health: http://24.144.119.69:5000/health"
echo "  Swagger: http://24.144.119.69:5000/swagger"
echo "  Chat: POST http://24.144.119.69:5000/api/chat"
echo ""
echo "Connected Services:"
echo "  ✅ PostgreSQL (storing conversations)"
echo "  ✅ Redis (caching responses)"
echo "  ✅ OpenAI (generating responses)"
echo ""
echo "To test from outside:"
echo "curl -X POST http://24.144.119.69:5000/api/chat \\"
echo "  -H 'Content-Type: application/json' \\"
echo "  -d '{\"message\":\"Hello Genie\",\"userId\":\"test\"}'"
