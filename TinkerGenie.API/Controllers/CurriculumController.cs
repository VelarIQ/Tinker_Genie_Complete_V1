using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Text.Json;
using Npgsql;

namespace TinkerGenie.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/curriculum")]
    public class CurriculumController : ControllerBase
    {
        private readonly string _connectionString;
        private readonly ILogger<CurriculumController> _logger;

        public CurriculumController(IConfiguration configuration, ILogger<CurriculumController> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
            _logger = logger;
        }

        [HttpGet("today/{userId}")]
        public async Task<IActionResult> GetTodaysPrompt(string userId)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                await using var userCmd = new NpgsqlCommand(@"
                    SELECT ud.current_day, up.communication_style, ud.signup_date 
                    FROM user_data ud
                    LEFT JOIN user_profiles up ON ud.user_id = up.user_id
                    WHERE ud.user_id = @userId", conn);
                userCmd.Parameters.AddWithValue("userId", userId);
                
                await using var userReader = await userCmd.ExecuteReaderAsync();
                if (!await userReader.ReadAsync())
                {
                    return NotFound(new { message = "User not found" });
                }
                
                var currentDay = userReader.GetInt32(0);
                var communicationStyle = userReader.IsDBNull(1) ? "balanced" : userReader.GetString(1);
                await userReader.CloseAsync();

                await using var promptCmd = new NpgsqlCommand(@"
                    SELECT id, day_number, prompt_title, prompt_text, fill_in_blanks, version
                    FROM leadership_daily_prompts
                    WHERE day_number = @dayNumber AND is_active = true
                    ORDER BY version DESC
                    LIMIT 1", conn);
                promptCmd.Parameters.AddWithValue("dayNumber", currentDay);
                
                await using var promptReader = await promptCmd.ExecuteReaderAsync();
                if (!await promptReader.ReadAsync())
                {
                    return Ok(new 
                    {
                        dayNumber = currentDay,
                        prompt = "Today, identify one system in your business that needs improvement. Write it down and the first step to fix it.",
                        title = "System Check",
                        style = communicationStyle
                    });
                }

                var promptId = promptReader.GetGuid(0);
                var dayNumber = promptReader.GetInt32(1);
                var title = promptReader.GetString(2);
                var promptText = promptReader.GetString(3);
                var fillInBlanks = promptReader.IsDBNull(4) ? null : promptReader.GetFieldValue<string[]>(4);
                var version = promptReader.GetInt32(5);

                var formattedPrompt = FormatPromptByStyle(promptText, title, dayNumber, communicationStyle);

                return Ok(new
                {
                    promptId = promptId,
                    dayNumber = dayNumber,
                    title = title,
                    prompt = formattedPrompt,
                    fillInBlanks = fillInBlanks,
                    version = version,
                    style = communicationStyle
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting todays prompt for user {UserId}", userId);
                return StatusCode(500, new { message = "Error retrieving prompt" });
            }
        }

        private string FormatPromptByStyle(string prompt, string title, int day, string style)
        {
            return style switch
            {
                "concise" => $"Day {day}: {prompt}",
                "balanced" => $"Day {day} - {title}\n\n{prompt}\n\nTake a moment to reflect.",
                "detailed" => $"Good morning! Welcome to Day {day} of your leadership journey.\n\n**{title}**\n\n{prompt}\n\nThis exercise will help you grow as a leader. Take your time, be honest with yourself, and remember - progress over perfection. You've got this!",
                _ => $"Day {day}: {prompt}"
            };
        }

        [HttpPost("complete")]
        public async Task<IActionResult> CompletePrompt([FromBody] PromptCompletionRequest request)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                await using var cmd = new NpgsqlCommand(@"
                    INSERT INTO user_prompt_progress 
                    (user_id, day_number, prompt_version, status, responses, completed_at)
                    VALUES (@userId, @dayNumber, @version, 'completed', @responses, CURRENT_TIMESTAMP)
                    ON CONFLICT (user_id, day_number) 
                    DO UPDATE SET 
                        status = 'completed',
                        responses = @responses,
                        completed_at = CURRENT_TIMESTAMP,
                        prompt_version = @version", conn);

                cmd.Parameters.AddWithValue("userId", request.UserId);
                cmd.Parameters.AddWithValue("dayNumber", request.DayNumber);
                cmd.Parameters.AddWithValue("version", request.Version);
                cmd.Parameters.AddWithValue("responses", JsonSerializer.Serialize(request.Responses));

                await cmd.ExecuteNonQueryAsync();

                await using var updateCmd = new NpgsqlCommand(@"
                    UPDATE user_data 
                    SET current_day = current_day + 1 
                    WHERE user_id = @userId", conn);
                updateCmd.Parameters.AddWithValue("userId", request.UserId);
                await updateCmd.ExecuteNonQueryAsync();

                return Ok(new { success = true, nextDay = request.DayNumber + 1 });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing prompt");
                return StatusCode(500, new { message = "Error saving completion" });
            }
        }
    }

    public class PromptCompletionRequest
    {
        public string UserId { get; set; } = "";
        public int DayNumber { get; set; }
        public int Version { get; set; }
        public Dictionary<string, string> Responses { get; set; } = new();
    }
}
