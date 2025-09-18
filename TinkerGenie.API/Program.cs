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

// Redis connection (single instance)
var redisConnectionString = builder.Configuration.GetConnectionString("ConnectionString") ?? "localhost:6379";
var redis = ConnectionMultiplexer.Connect(redisConnectionString);
builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

// Add SignalR and configure Redis Backplane
builder.Services.AddSignalR().AddStackExchangeRedis(redisConnectionString);

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
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// Map SignalR Hubs
app.MapHub<ChatHub>("/chatHub").RequireAuthorization();
app.MapHub<SyncHub>("/syncHub").RequireAuthorization();

app.MapControllers();

app.Run();
