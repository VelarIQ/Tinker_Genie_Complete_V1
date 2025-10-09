using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Npgsql;
using System.Text.Json;

namespace TinkerGenie.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class UserPreferencesController : ControllerBase
    {
        private readonly ILogger<UserPreferencesController> _logger;
        private readonly string _connectionString;

        public UserPreferencesController(ILogger<UserPreferencesController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
        }

        [HttpGet]
        public async Task<IActionResult> GetPreferences()
        {
            try
            {
                var userId = User.FindFirst("user_id")?.Value ?? User.FindFirst("sub")?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized();
                }

                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                var cmd = new NpgsqlCommand(@"
                    SELECT 
                        communication_style, 
                        communication_tone,
                        preferred_response_length, 
                        timezone,
                        preferred_prompt_time,
                        nudge_enabled,
                        'dark' as theme
                    FROM user_profiles 
                    WHERE user_id = @userId
                    LIMIT 1", conn);

                cmd.Parameters.AddWithValue("userId", userId);

                await using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return Ok(new
                    {
                        communicationStyle = reader.IsDBNull(0) ? "balanced" : reader.GetString(0),
                        communicationTone = reader.IsDBNull(1) ? "professional" : reader.GetString(1),
                        preferredResponseLength = reader.IsDBNull(2) ? "medium" : reader.GetString(2),
                        timezone = reader.IsDBNull(3) ? "America/New_York" : reader.GetString(3),
                        dailyPromptTime = reader.IsDBNull(4) ? "09:00" : reader.GetFieldValue<TimeOnly>(4).ToString("HH:mm"),
                        notificationsEnabled = reader.IsDBNull(5) || reader.GetBoolean(5),
                        theme = reader.IsDBNull(6) ? "dark" : reader.GetString(6)
                    });
                }

                // Return defaults if no preferences exist
                return Ok(new
                {
                    communicationStyle = "balanced",
                    communicationTone = "professional",
                    preferredResponseLength = "medium",
                    timezone = "America/New_York",
                    dailyPromptTime = "09:00",
                    notificationsEnabled = true,
                    theme = "dark"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user preferences");
                return StatusCode(500, new { error = "Failed to get preferences" });
            }
        }

        [HttpPut]
        public async Task<IActionResult> UpdatePreferences([FromBody] UpdatePreferencesRequest request)
        {
            try
            {
                var userId = User.FindFirst("user_id")?.Value ?? User.FindFirst("sub")?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized();
                }

                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                // Update user_preferences table
                var cmd = new NpgsqlCommand(@"
                    INSERT INTO user_preferences (
                        user_id, 
                        communication_style, 
                        communication_tone,
                        theme, 
                        daily_prompt_time,
                        notifications_enabled,
                        timezone,
                        updated_at
                    ) VALUES (
                        @userId, 
                        @style, 
                        @tone,
                        @theme, 
                        @promptTime::time,
                        @notifications,
                        @timezone,
                        NOW()
                    )
                    ON CONFLICT (user_id) DO UPDATE SET
                        communication_style = @style,
                        communication_tone = @tone,
                        theme = @theme,
                        daily_prompt_time = @promptTime::time,
                        notifications_enabled = @notifications,
                        timezone = @timezone,
                        updated_at = NOW()", conn);

                cmd.Parameters.AddWithValue("userId", Guid.Parse(userId));
                cmd.Parameters.AddWithValue("style", request.CommunicationStyle ?? "balanced");
                cmd.Parameters.AddWithValue("tone", request.CommunicationTone ?? "professional");
                cmd.Parameters.AddWithValue("theme", request.Theme ?? "dark");
                cmd.Parameters.AddWithValue("promptTime", request.DailyPromptTime ?? "09:00");
                cmd.Parameters.AddWithValue("notifications", request.NotificationsEnabled ?? true);
                cmd.Parameters.AddWithValue("timezone", request.Timezone ?? "America/New_York");

                await cmd.ExecuteNonQueryAsync();

                // Also update user_profiles table for consistency
                var profileCmd = new NpgsqlCommand(@"
                    UPDATE user_profiles 
                    SET 
                        communication_style = @style,
                        communication_tone = @tone,
                        timezone = @timezone,
                        updated_at = NOW()
                    WHERE user_id = @userId", conn);

                profileCmd.Parameters.AddWithValue("userId", userId);
                profileCmd.Parameters.AddWithValue("style", request.CommunicationStyle ?? "balanced");
                profileCmd.Parameters.AddWithValue("tone", request.CommunicationTone ?? "professional");
                profileCmd.Parameters.AddWithValue("timezone", request.Timezone ?? "America/New_York");

                await profileCmd.ExecuteNonQueryAsync();

                _logger.LogInformation("Updated preferences for user {UserId}", userId);

                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating user preferences");
                return StatusCode(500, new { error = "Failed to update preferences" });
            }
        }
    }

    public class UpdatePreferencesRequest
    {
        public string? CommunicationStyle { get; set; }
        public string? CommunicationTone { get; set; }
        public string? Theme { get; set; }
        public string? DailyPromptTime { get; set; }
        public bool? NotificationsEnabled { get; set; }
        public string? Timezone { get; set; }
    }
}
