using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Threading.Tasks;
using Npgsql;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace TinkerGenie.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PreferencesController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<PreferencesController> _logger;
        private readonly string _connectionString;

        public PreferencesController(IConfiguration configuration, ILogger<PreferencesController> logger)
        {
            _configuration = configuration;
            _logger = logger;
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
        }

        [HttpGet("{userId}")]
        public async Task<IActionResult> GetPreferences(string userId)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                // Check if user has preferences stored
                var cmd = new NpgsqlCommand(@"
                    SELECT 
                        u.id, u.email, u.name,
                        up.current_day,
                        COALESCE(pref.communication_style, 'balanced') as communication_style,
                        COALESCE(pref.prompt_delivery_time, '09:00:00') as prompt_time,
                        COALESCE(pref.timezone, 'America/New_York') as timezone,
                        CASE WHEN pref.id IS NOT NULL THEN true ELSE false END as onboarding_completed
                    FROM user_data u
                    LEFT JOIN user_profiles up ON u.id::text = up.user_id::text
                    LEFT JOIN user_preferences pref ON u.id = pref.user_id
                    WHERE u.id::text = $1 OR u.email = $1 OR u.user_id = $1", conn);

                cmd.Parameters.AddWithValue(userId);

                await using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    // Map database communication style to frontend values
                    var dbStyle = reader.GetString(4);
                    var frontendStyle = dbStyle switch
                    {
                        "short" => "concise",
                        "medium" => "balanced",
                        "long" => "detailed",
                        _ => "balanced"
                    };
                    
                    return Ok(new 
                    {
                        userId = userId,
                        name = reader.IsDBNull(2) ? "Leader" : reader.GetString(2),
                        currentDay = reader.IsDBNull(3) ? 1 : reader.GetInt32(3),
                        communicationStyle = frontendStyle,
                        promptTime = reader.GetTimeSpan(5).ToString(@"hh\:mm"),
                        timezone = reader.GetString(6),
                        onboardingCompleted = reader.GetBoolean(7),
                        theme = "light",
                        notifications = true,
                        language = "en"
                    });
                }

                // Default response for new users
                return Ok(new 
                {
                    userId = userId,
                    name = "Leader",
                    currentDay = 1,
                    communicationStyle = "balanced",
                    promptTime = "09:00",
                    timezone = "America/New_York",
                    onboardingCompleted = false,
                    theme = "light",
                    notifications = true,
                    language = "en"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting preferences for user {UserId}", userId);
                return StatusCode(500, new { error = ex.Message });
            }
        }
        
        [HttpPost("{userId}")]
        public async Task<IActionResult> UpdatePreferences(string userId, [FromBody] dynamic preferences)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                // Get user ID from database - search by the actual userId parameter
                var getUserCmd = new NpgsqlCommand(@"
                    SELECT id FROM user_data 
                    WHERE id::text = $1 
                       OR email = $1 
                       OR user_id = $1", conn);
                getUserCmd.Parameters.AddWithValue(userId);
                
                var userGuid = await getUserCmd.ExecuteScalarAsync();
                _logger.LogInformation("Looking for user {UserId}, found GUID: {UserGuid}", userId, userGuid);
                
                if (userGuid == null)
                {
                    // Create user if they don't exist
                    var createUserCmd = new NpgsqlCommand(@"
                        INSERT INTO user_data (id, user_id, name, email, current_day, password_hash)
                        VALUES ($1, $2, $3, $4, $5, $6)
                        RETURNING id", conn);
                    
                    var newUserId = Guid.NewGuid();
                    var defaultPasswordHash = HashPassword("TinkerGenie2025!");
                    
                    createUserCmd.Parameters.AddWithValue(newUserId);
                    createUserCmd.Parameters.AddWithValue(userId);
                    createUserCmd.Parameters.AddWithValue("New User");
                    createUserCmd.Parameters.AddWithValue(userId.Contains("@") ? userId : $"{userId}@tinkergenie.com");
                    createUserCmd.Parameters.AddWithValue(1);
                    createUserCmd.Parameters.AddWithValue(defaultPasswordHash);
                    
                    userGuid = await createUserCmd.ExecuteScalarAsync();
                    
                    if (userGuid == null)
                    {
                        _logger.LogError("Failed to create user {UserId}", userId);
                        return StatusCode(500, new { message = "Failed to create user" });
                    }
                    _logger.LogInformation("Created new user {UserId} with GUID: {UserGuid}", userId, userGuid);
                }

                // Insert or update preferences
                var cmd = new NpgsqlCommand(@"
                    INSERT INTO user_preferences 
                    (id, user_id, communication_style, prompt_delivery_time, timezone, created_at, updated_at)
                    VALUES 
                    ($1, $2, $3, $4::time, $5, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
                    ON CONFLICT (user_id) 
                    DO UPDATE SET 
                        communication_style = $3,
                        prompt_delivery_time = $4::time,
                        timezone = $5,
                        updated_at = CURRENT_TIMESTAMP", conn);

                var prefsJson = preferences?.ToString() ?? "{}";
                dynamic prefs = JsonSerializer.Deserialize<JsonElement>(prefsJson);
                
                // Map frontend communication styles to database values
                var frontendStyle = "balanced";
                var promptTime = "09:00";
                var timezone = "America/New_York";
                
                if (prefs.TryGetProperty("communicationStyle", out JsonElement styleProp))
                {
                    frontendStyle = styleProp.GetString() ?? "balanced";
                }
                
                if (prefs.TryGetProperty("promptTime", out JsonElement timeProp))
                {
                    promptTime = timeProp.GetString() ?? "09:00";
                }
                
                if (prefs.TryGetProperty("timezone", out JsonElement tzProp))
                {
                    timezone = tzProp.GetString() ?? "America/New_York";
                }
                
                var dbStyle = frontendStyle switch
                {
                    "concise" => "short",
                    "balanced" => "medium",
                    "detailed" => "long",
                    _ => "medium"
                };
                
                cmd.Parameters.AddWithValue(Guid.NewGuid());
                cmd.Parameters.AddWithValue((Guid)userGuid);
                cmd.Parameters.AddWithValue(dbStyle);
                cmd.Parameters.AddWithValue(promptTime);
                cmd.Parameters.AddWithValue(timezone);

                await cmd.ExecuteNonQueryAsync();

                return Ok(new { success = true, message = "Preferences updated" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating preferences for user {UserId}", userId);
                return StatusCode(500, new { error = ex.Message, details = ex.ToString() });
            }
        }

        [HttpPost]
        public async Task<IActionResult> SavePreferences([FromBody] dynamic preferences)
        {
            try
            {
                var prefsJson = preferences?.ToString() ?? "{}";
                dynamic prefs = JsonSerializer.Deserialize<JsonElement>(prefsJson);
                
                var userId = prefs.GetProperty("userId").GetString();
                return await UpdatePreferences(userId, preferences);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving preferences");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        private string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password + "TinkerGenieSalt2025"));
            return Convert.ToBase64String(hashedBytes);
        }
    }
}
