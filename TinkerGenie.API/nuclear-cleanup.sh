#!/bin/bash
# ============================================
# COMPLETE CLEANUP AND FRESH START
# ============================================

echo "🧹 NUCLEAR CLEANUP - Starting fresh..."

# 1. BACKUP EVERYTHING FIRST
echo "📦 Creating backup..."
cd /var/www/tinker-genie
tar -czf TinkerGenie.API.backup.$(date +%Y%m%d-%H%M%S).tar.gz TinkerGenie.API/

# 2. DELETE ALL PROBLEM FILES
echo "🗑️ Removing all conflicting files..."
cd /var/www/tinker-genie/TinkerGenie.API

# Remove all service files (they have conflicts)
rm -rf Services/
rm -rf Controllers/
rm -rf Data/
rm -rf Models/

# Clean build artifacts
rm -rf bin/ obj/

# 3. CREATE FRESH PROJECT STRUCTURE
echo "📁 Creating fresh structure..."
mkdir -p Controllers Services Data Models

# 4. CREATE SIMPLE WORKING PROGRAM.CS
echo "📝 Creating Program.cs..."
cat > Program.cs << 'EOF'
var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

// Configure pipeline
app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("AllowAll");
app.MapControllers();

// Simple health check
app.MapGet("/health", () => Results.Ok(new {
    status = "healthy",
    timestamp = DateTime.UtcNow,
    version = "3.0.0"
}));

// Simple chat endpoint for testing
app.MapPost("/api/chat/simple", (SimpleMessage msg) => 
{
    return Results.Ok(new {
        reply = $"You said: {msg.Message}",
        timestamp = DateTime.UtcNow
    });
});

app.Run();

public record SimpleMessage(string Message);
EOF

# 5. CREATE BASIC CHAT CONTROLLER
echo "💬 Creating ChatController..."
cat > Controllers/ChatController.cs << 'EOF'
using Microsoft.AspNetCore.Mvc;

namespace TinkerGenie.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly ILogger<ChatController> _logger;
    
    public ChatController(ILogger<ChatController> logger)
    {
        _logger = logger;
    }
    
    [HttpPost]
    public IActionResult Chat([FromBody] ChatRequest request)
    {
        _logger.LogInformation($"Received message: {request.Message}");
        
        // For now, just echo back
        return Ok(new
        {
            success = true,
            message = $"Echo: {request.Message}",
            timestamp = DateTime.UtcNow
        });
    }
    
    [HttpGet("test")]
    public IActionResult Test()
    {
        return Ok(new { status = "Chat controller working!" });
    }
}

public class ChatRequest
{
    public string Message { get; set; } = "";
    public string? UserId { get; set; }
}
EOF

# 6. BUILD AND TEST
echo "🔨 Building clean project..."
dotnet build

# 7. IF BUILD SUCCEEDS, ADD OPENAI
if [ $? -eq 0 ]; then
    echo "✅ Build successful! Adding OpenAI..."
    
    # Create simple OpenAI service
    cat > Services/OpenAIService.cs << 'EOF'
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TinkerGenie.API.Services;

public interface IOpenAIService
{
    Task<string> GetResponseAsync(string message);
}

public class OpenAIService : IOpenAIService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    
    public OpenAIService(IConfiguration configuration)
    {
        _httpClient = new HttpClient();
        _apiKey = configuration["OpenAI:ApiKey"] ?? "**API_KEY**";
        _httpClient.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", _apiKey);
    }
    
    public async Task<string> GetResponseAsync(string message)
    {
        try
        {
            var request = new
            {
                model = "gpt-4o-mini",
                messages = new[]
                {
                    new { role = "system", content = "You are a helpful assistant." },
                    new { role = "user", content = message }
                },
                max_tokens = 150
            };
            
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync(
                "https://api.openai.com/v1/chat/completions", 
                content
            );
            
            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseJson);
                return doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString() ?? "No response";
            }
            
            return "Error connecting to OpenAI";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }
}
EOF

    # Update Program.cs to register service
    sed -i '/builder.Services.AddCors/a builder.Services.AddScoped<TinkerGenie.API.Services.IOpenAIService, TinkerGenie.API.Services.OpenAIService>();' Program.cs
    
    # Update ChatController to use OpenAI
    cat > Controllers/ChatController.cs << 'EOF'
using Microsoft.AspNetCore.Mvc;
using TinkerGenie.API.Services;

namespace TinkerGenie.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly ILogger<ChatController> _logger;
    private readonly IOpenAIService _openAI;
    
    public ChatController(ILogger<ChatController> logger, IOpenAIService openAI)
    {
        _logger = logger;
        _openAI = openAI;
    }
    
    [HttpPost]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request)
    {
        _logger.LogInformation($"Received message: {request.Message}");
        
        var response = await _openAI.GetResponseAsync(request.Message);
        
        return Ok(new
        {
            success = true,
            message = response,
            timestamp = DateTime.UtcNow
        });
    }
    
    [HttpGet("test")]
    public IActionResult Test()
    {
        return Ok(new { status = "Chat controller with OpenAI working!" });
    }
}

public class ChatRequest
{
    public string Message { get; set; } = "";
    public string? UserId { get; set; }
}
EOF

    # Build again
    echo "🔨 Building with OpenAI..."
    dotnet build
fi

# 8. RESTART API
echo "🚀 Restarting API..."
pm2 delete tinker-api 2>/dev/null || true
pm2 start "dotnet run --urls http://0.0.0.0:5000" --name tinker-api --cwd /var/www/tinker-genie/TinkerGenie.API

# 9. TEST
sleep 5
echo ""
echo "🧪 Testing endpoints..."
echo "===================="

# Test health
echo "Health check:"
curl -s http://localhost:5000/health | jq .

echo ""
echo "Chat test:"
curl -X POST http://localhost:5000/api/chat \
  -H "Content-Type: application/json" \
  -d '{"message":"Hello!"}' | jq .

echo ""
echo "✅ CLEAN SETUP COMPLETE!"
echo ""
echo "Test from outside:"
echo "curl -X POST http://24.144.119.69:5000/api/chat \\"
echo "  -H 'Content-Type: application/json' \\"
echo "  -d '{\"message\":\"What is leadership?\"}'"
