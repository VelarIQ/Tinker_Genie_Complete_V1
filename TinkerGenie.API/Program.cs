using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using TinkerGenie.API.Services;
using StackExchange.Redis;
using TinkerGenie.API.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
    {
        "application/octet-stream" // for SignalR binary protocol
    });
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add SignalR with recommended options (longer keepalive and handshake timeouts under proxies)
builder.Services.AddSignalR(options =>
{
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
    options.HandshakeTimeout = TimeSpan.FromSeconds(15);
});

// Redis connection (single instance)
var redisConnectionString = builder.Configuration.GetSection("Redis")["ConnectionString"]
    ?? builder.Configuration.GetConnectionString("ConnectionString")
    ?? "localhost:6379";
var redis = ConnectionMultiplexer.Connect(redisConnectionString);
builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
// SignalR Redis backplane for scale-out
builder.Services.AddSignalR().AddStackExchangeRedis(redis, options =>
{
    options.Configuration.ChannelPrefix = "tinker-genie";
});

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
app.UseResponseCompression();

// Configure pipeline - CORRECT ORDER
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Honor reverse proxy headers for correct scheme/remote IP when behind load balancers
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseHttpsRedirection();
app.UseCors("AllowTinker");
app.UseRouting();           // MUST come first
app.UseAuthentication();    // Then authentication
app.UseAuthorization();     // Then authorization

// Map SignalR Hubs
app.MapHub<ChatHub>("/chatHub").RequireAuthorization();
app.MapHub<SyncHub>("/syncHub").RequireAuthorization();

// Do not override SignalR negotiate endpoints; MapHub provides proper negotiate handling

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