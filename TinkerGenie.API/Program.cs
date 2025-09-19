using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using TinkerGenie.API.Services;
using StackExchange.Redis;
using TinkerGenie.API.Hubs;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add SignalR
builder.Services.AddSignalR();

// Redis connection (single instance)
var redisConnectionString = builder.Configuration.GetConnectionString("ConnectionString") ?? "localhost:6379";
var redis = ConnectionMultiplexer.Connect(redisConnectionString);
builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

// Register all services that exist
builder.Services.AddScoped<IOpenAIService, OpenAIService>();
builder.Services.AddScoped<IConversationService, ConversationService>();
builder.Services.AddScoped<IWeaviateService, WeaviateService>();
builder.Services.AddScoped<IUserDataIsolationService, UserDataIsolationService>();

// Add Authentication with the TBB-provided JWT secret
var jwtKey = "TinkerGenieJWTSecretKey2025VeryLongAndSecure";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ClockSkew = TimeSpan.Zero
        };
        
        // Extract JWT token from query string for WebSocket connections
        options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.HttpContext.Request.Path.StartsWithSegments("/chatHub") || 
                    context.HttpContext.Request.Path.StartsWithSegments("/syncHub"))
                {
                    var accessToken = context.Request.Query["access_token"];
                    if (!string.IsNullOrEmpty(accessToken))
                    {
                        context.Token = accessToken;
                        Console.WriteLine($"JWT token extracted from query string for {context.HttpContext.Request.Path}");
                    }
                }
                return Task.CompletedTask;
            }
        };
    });

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowTinker", policy =>
    {
        policy.WithOrigins("https://tinker.twobrain.ai")
        .AllowAnyMethod()
        .AllowAnyHeader()
        .AllowCredentials();
    });
});

var app = builder.Build();

// Configure pipeline - CORRECT ORDER
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowTinker");
app.UseRouting();           // MUST come first
app.UseAuthentication();    // Then authentication
app.UseAuthorization();     // Then authorization

// Map SignalR Hubs
app.MapHub<ChatHub>("/chatHub").RequireAuthorization();
app.MapHub<SyncHub>("/syncHub").RequireAuthorization();

// Allow SignalR negotiation without authentication
app.MapPost("/chatHub/negotiate", (HttpContext context) =>
{
    // SignalR negotiation endpoint - no auth required
    return Results.Ok();
});

app.MapPost("/syncHub/negotiate", (HttpContext context) =>
{
    // SignalR negotiation endpoint - no auth required
    return Results.Ok();
});

app.MapControllers();       // Finally controllers

// Health endpoint
app.MapGet("/health", () => Results.Ok(new {
    status = "healthy",
    timestamp = DateTime.UtcNow,
    version = "1.0.0"
}));

// OpenAI health check endpoint
app.MapGet("/api/health", () =>
{
    return Results.Ok(new { 
        ok = true, 
        status = "healthy",
        timestamp = DateTime.UtcNow,
        services = new {
            api = "running",
            weaviate = "configured",
            redis = "configured"
        }
    });
});

// Status endpoint for monitoring
app.MapGet("/api/status", () =>
{
    return Results.Ok(new
    {
        service = "TinkerGenie API",
        status = "running",
        timestamp = DateTime.UtcNow,
        version = "1.0.0",
        endpoints = new
        {
            health = "/api/health",
            chat = "/api/chat",
            signalr = "/chatHub"
        }
    });
});

app.Run();

public class ChatRequest
{
    public string Message { get; set; } = "";
    public string? UserEmail { get; set; }
}