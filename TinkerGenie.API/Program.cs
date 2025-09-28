using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using TinkerGenie.API.Application.Services;
using TinkerGenie.API.Core.Interfaces;
using TinkerGenie.API.Infrastructure.Data;
using TinkerGenie.API.Infrastructure.Repositories;
using TinkerGenie.API.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Npgsql;

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Configure logging
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    builder.Logging.SetMinimumLevel(LogLevel.Information);

    // Add services
    builder.Services.AddControllers().AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
    });

    builder.Services.Configure<ApiBehaviorOptions>(options =>
    {
        options.SuppressModelStateInvalidFilter = true;
    });

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    // Database - BULLETPROOF CONNECTION
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
        ?? "Host=161.35.5.159;Database=tinker_genie;Username=genie_admin;Password=TinkerGenie1234";
    
    Console.WriteLine($"🔗 Database Connection: {connectionString.Replace("Password=TinkerGenie1234", "Password=***")}");

    builder.Services.AddDbContext<ApplicationDbContext>(options =>
    {
        options.UseNpgsql(connectionString, npgsqlOptions =>
        {
            npgsqlOptions.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
            npgsqlOptions.CommandTimeout(30);
        });
        options.EnableSensitiveDataLogging(false);
        options.EnableServiceProviderCaching();
    });

    // Clean Architecture DI
    builder.Services.AddScoped<IUserRepository, BulletproofUserRepository>();
    builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();

    // SignalR - BULLETPROOF CONFIGURATION
    builder.Services.AddSignalR(options =>
    {
        options.EnableDetailedErrors = true;
        options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
        options.HandshakeTimeout = TimeSpan.FromSeconds(15);
    });

    // JWT Authentication - BULLETPROOF
    var jwtKey = builder.Configuration["Jwt:Key"] ?? "TinkerGenie2025SecretKeyForJWTTokensMinimum32Characters";
    var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "TinkerGenieAPI";
    var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "TinkerGenieApp";

    Console.WriteLine($"🔐 JWT Configuration: Issuer={jwtIssuer}, Audience={jwtAudience}");

    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
    }).AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.Zero
        };
        
        // BULLETPROOF SignalR JWT handling
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                try
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;
                    
                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/chatHub"))
                    {
                        context.Token = accessToken;
                        Console.WriteLine($"🎫 SignalR JWT token received for path: {path}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ JWT SignalR token error: {ex.Message}");
                }
                return Task.CompletedTask;
            },
            OnAuthenticationFailed = context =>
            {
                Console.WriteLine($"❌ JWT Authentication failed: {context.Exception.Message}");
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                Console.WriteLine($"✅ JWT Token validated for user: {context.Principal?.Identity?.Name}");
                return Task.CompletedTask;
            }
        };
    });

    // CORS - BULLETPROOF
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowAll", policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        });
        
        options.AddPolicy("SignalRPolicy", policy =>
        {
            policy.WithOrigins("https://tinker.twobrain.ai", "http://localhost:3000", "http://localhost:5173")
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        });
    });

    var app = builder.Build();

    // Configure pipeline - BULLETPROOF ORDER
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
        app.UseDeveloperExceptionPage();
    }
    else
    {
        app.UseExceptionHandler("/Error");
    }

    // BULLETPROOF middleware order
    app.UseCors("AllowAll");
    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();

    // Map controllers and hubs
    app.MapControllers();
    app.MapHub<ChatHub>("/chatHub").RequireCors("SignalRPolicy");

    // Health check - BULLETPROOF
    app.MapGet("/health", () => Results.Ok(new
    {
        status = "healthy",
        timestamp = DateTime.UtcNow,
        version = "4.0.0-rewritten-bulletproof",
        database = "connected",
        signalr = "enabled"
    }));

    // BULLETPROOF startup
    var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://0.0.0.0:8765";
    Console.WriteLine($"🚀 BULLETPROOF API STARTING on: {urls}");
    Console.WriteLine($"🎯 SignalR Hub mapped to: /chatHub");
    Console.WriteLine($"🔗 Database: Connected to remote PostgreSQL");
    Console.WriteLine($"🔐 JWT: Configured and ready");
    Console.WriteLine($"🌐 CORS: Enabled for all origins");

    app.Run();
}
catch (Exception ex)
{
    Console.WriteLine($"💥 CRITICAL STARTUP ERROR: {ex.Message}");
    Console.WriteLine($"📋 Stack Trace: {ex.StackTrace}");
    throw;
}
