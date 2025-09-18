using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Text.Json;
using Npgsql;

namespace TinkerGenie.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/genie")]
    public class GenieController : ControllerBase
    {
        private readonly string _connectionString;
        private readonly ILogger<GenieController> _logger;

        public GenieController(IConfiguration configuration, ILogger<GenieController> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
            _logger = logger;
        }

        [HttpGet("current-day")]
        public async Task<IActionResult> GetCurrentDay([FromQuery] string userId, [FromQuery] string genieType = "tinker")
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                // Get user's current day and progress
                await using var userCmd = new NpgsqlCommand(@"
                    SELECT 
                        ud.current_day,
                        ud.signup_date,
                        up.communication_style,
                        up.timezone,
                        up.prompt_time
                    FROM user_data ud
                    LEFT JOIN user_profiles up ON ud.user_id = up.user_id
                    WHERE ud.user_id = @userId", conn);
                userCmd.Parameters.AddWithValue("userId", userId);
                
                await using var userReader = await userCmd.ExecuteReaderAsync();
                if (!await userReader.ReadAsync())
                {
                    // User doesn't exist, create default user
                    await userReader.CloseAsync();
                    return Ok(new
                    {
                        currentDay = 1,
                        genieType = genieType,
                        isNewUser = true,
                        communicationStyle = "balanced",
                        timezone = "UTC",
                        promptTime = "09:00"
                    });
                }
                
                var currentDay = userReader.GetInt32(0);
                var signupDate = userReader.GetDateTime(1);
                var communicationStyle = userReader.IsDBNull(2) ? "balanced" : userReader.GetString(2);
                var timezone = userReader.IsDBNull(3) ? "UTC" : userReader.GetString(3);
                var promptTime = userReader.IsDBNull(4) ? "09:00" : userReader.GetString(4);
                await userReader.CloseAsync();

                // Get today's prompt
                await using var promptCmd = new NpgsqlCommand(@"
                    SELECT id, day_number, prompt_title, prompt_text, fill_in_blanks, version
                    FROM leadership_daily_prompts
                    WHERE day_number = @dayNumber AND is_active = true
                    ORDER BY version DESC
                    LIMIT 1", conn);
                promptCmd.Parameters.AddWithValue("dayNumber", currentDay);
                
                await using var promptReader = await promptCmd.ExecuteReaderAsync();
                string? todaysPrompt = null;
                string? promptTitle = null;
                if (await promptReader.ReadAsync())
                {
                    promptTitle = promptReader.GetString(2);
                    todaysPrompt = promptReader.GetString(3);
                }
                await promptReader.CloseAsync();

                // Get user's progress for today
                await using var progressCmd = new NpgsqlCommand(@"
                    SELECT status, responses, completed_at
                    FROM user_prompt_progress
                    WHERE user_id = @userId AND day_number = @dayNumber", conn);
                progressCmd.Parameters.AddWithValue("userId", userId);
                progressCmd.Parameters.AddWithValue("dayNumber", currentDay);
                
                await using var progressReader = await progressCmd.ExecuteReaderAsync();
                bool isCompleted = false;
                DateTime? completedAt = null;
                if (await progressReader.ReadAsync())
                {
                    isCompleted = progressReader.GetString(0) == "completed";
                        completedAt = progressReader.IsDBNull(2) ? (DateTime?)null : progressReader.GetDateTime(2);
                }
                await progressReader.CloseAsync();

                return Ok(new
                {
                    currentDay = currentDay,
                    genieType = genieType,
                    isNewUser = false,
                    communicationStyle = communicationStyle,
                    timezone = timezone,
                    promptTime = promptTime,
                    signupDate = signupDate,
                    todaysPrompt = todaysPrompt ?? "Welcome to your leadership journey! Today, take a moment to reflect on your goals and what you want to achieve.",
                    promptTitle = promptTitle ?? "Daily Leadership Challenge",
                    isCompleted = isCompleted,
                    completedAt = completedAt,
                    daysSinceSignup = (DateTime.UtcNow - signupDate).Days
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting current day for user {UserId}", userId);
                return StatusCode(500, new { message = "Error retrieving current day information" });
            }
        }

        [HttpPost("advance-day")]
        public async Task<IActionResult> AdvanceDay([FromBody] AdvanceDayRequest request)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                await using var cmd = new NpgsqlCommand(@"
                    UPDATE user_data 
                    SET current_day = current_day + 1 
                    WHERE user_id = @userId", conn);
                cmd.Parameters.AddWithValue("userId", request.UserId);
                
                var rowsAffected = await cmd.ExecuteNonQueryAsync();
                
                if (rowsAffected == 0)
                {
                    return NotFound(new { message = "User not found" });
                }

                return Ok(new { success = true, message = "Day advanced successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error advancing day for user {UserId}", request.UserId);
                return StatusCode(500, new { message = "Error advancing day" });
            }
        }

        [HttpGet("progress/{userId}")]
        public async Task<IActionResult> GetUserProgress(string userId)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                await using var cmd = new NpgsqlCommand(@"
                    SELECT 
                        day_number,
                        status,
                        completed_at,
                        prompt_version
                    FROM user_prompt_progress
                    WHERE user_id = @userId
                    ORDER BY day_number DESC", conn);
                cmd.Parameters.AddWithValue("userId", userId);

                var progress = new List<object>();
                await using var reader = await cmd.ExecuteReaderAsync();
                
                while (await reader.ReadAsync())
                {
                    progress.Add(new
                    {
                        dayNumber = reader.GetInt32(0),
                        status = reader.GetString(1),
                        completedAt = reader.IsDBNull(2) ? (DateTime?)null : reader.GetDateTime(2),
                        promptVersion = reader.GetInt32(3)
                    });
                }

                return Ok(new { progress, totalDays = progress.Count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving progress for user {UserId}", userId);
                return StatusCode(500, new { message = "Error retrieving progress" });
            }
        }
    }

    public class AdvanceDayRequest
    {
        public string UserId { get; set; } = "";
    }
}
