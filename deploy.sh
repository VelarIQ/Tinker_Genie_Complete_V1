#!/bin/bash

# TinkerGenie Backend Deployment Script v2
# Run: chmod +x deploy-backend-v2.sh && ./deploy-backend-v2.sh

echo "🚀 Starting TinkerGenie backend deployment..."

cd /var/www/tinker-genie-clean/TinkerGenie.API || exit 1

# 1. Create PromptsController
echo "Creating PromptsController..."
cat > Controllers/PromptsController.cs << 'EOF'
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Npgsql;
using StackExchange.Redis;
using System.Text.Json;

namespace TinkerGenie.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PromptsController : ControllerBase
    {
        private readonly string _connectionString;
        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<PromptsController> _logger;
        
        public PromptsController(IConfiguration configuration, IConnectionMultiplexer redis, ILogger<PromptsController> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
            _redis = redis;
            _logger = logger;
        }
        
        [HttpGet("day/{dayNumber}")]
        public async Task<IActionResult> GetDailyPrompt(int dayNumber)
        {
            try
            {
                var db = _redis.GetDatabase();
                var cacheKey = $"prompt:day:{dayNumber}";
                var cached = await db.StringGetAsync(cacheKey);
                
                if (!cached.IsNullOrEmpty)
                {
                    return Ok(JsonSerializer.Deserialize<DailyPromptResponse>(cached!));
                }
                
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                
                var cmd = new NpgsqlCommand(@"
                    SELECT day_number, prompt_title, prompt_text, category, difficulty
                    FROM leadership_daily_prompts WHERE day_number = @dayNumber", conn);
                cmd.Parameters.AddWithValue("dayNumber", dayNumber);
                
                await using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var response = new DailyPromptResponse
                    {
                        DayNumber = reader.GetInt32(0),
                        Title = reader.GetString(1),
                        PromptText = reader.GetString(2),
                        Category = reader.IsDBNull(3) ? null : reader.GetString(3),
                        Difficulty = reader.IsDBNull(4) ? null : reader.GetString(4)
                    };
                    
                    await db.StringSetAsync(cacheKey, JsonSerializer.Serialize(response), TimeSpan.FromHours(24));
                    return Ok(response);
                }
                
                return NotFound(new { message = $"Prompt for day {dayNumber} not found" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching prompt for day {DayNumber}", dayNumber);
                return StatusCode(500, new { error = "Failed to fetch prompt" });
            }
        }
        
        [HttpGet("next")]
        public async Task<IActionResult> GetNextPrompt()
        {
            try
            {
                var userId = User.FindFirst("userId")?.Value ?? "";
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                
                var cmd = new NpgsqlCommand(@"
                    SELECT current_day FROM user_data
                    WHERE user_id = @userId OR id::text = @userId", conn);
                cmd.Parameters.AddWithValue("userId", userId);
                
                var currentDay = await cmd.ExecuteScalarAsync();
                if (currentDay == null) return NotFound();
                
                return await GetDailyPrompt(Convert.ToInt32(currentDay) + 1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching next prompt");
                return StatusCode(500, new { error = "Failed to fetch next prompt" });
            }
        }
        
        [HttpPost("advance")]
        public async Task<IActionResult> AdvanceDay()
        {
            try
            {
                var userId = User.FindFirst("userId")?.Value ?? "";
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                using var transaction = await conn.BeginTransactionAsync();
                
                var getCurrentCmd = new NpgsqlCommand(@"
                    SELECT current_day FROM user_data
                    WHERE user_id = @userId OR id::text = @userId", conn, transaction);
                getCurrentCmd.Parameters.AddWithValue("userId", userId);
                
                var currentDay = await getCurrentCmd.ExecuteScalarAsync();
                if (currentDay == null) return NotFound();
                
                var completedDay = Convert.ToInt32(currentDay);
                var nextDay = completedDay + 1;
                
                var updateCmd = new NpgsqlCommand(@"
                    UPDATE user_data SET current_day = @nextDay, last_prompt_date = CURRENT_DATE
                    WHERE user_id = @userId OR id::text = @userId", conn, transaction);
                updateCmd.Parameters.AddWithValue("userId", userId);
                updateCmd.Parameters.AddWithValue("nextDay", nextDay);
                await updateCmd.ExecuteNonQueryAsync();
                
                await transaction.CommitAsync();
                
                var db = _redis.GetDatabase();
                await db.KeyDeleteAsync($"chat:session:{userId}");
                
                return Ok(new { success = true, completedDay, nextDay, message = $"Advanced to Day {nextDay}" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error advancing day");
                return StatusCode(500, new { error = "Failed to advance day" });
            }
        }
        
        [HttpGet("progress")]
        public async Task<IActionResult> GetProgress()
        {
            try
            {
                var userId = User.FindFirst("userId")?.Value ?? "";
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                
                var cmd = new NpgsqlCommand(@"
                    SELECT current_day, last_prompt_date FROM user_data
                    WHERE user_id = @userId OR id::text = @userId", conn);
                cmd.Parameters.AddWithValue("userId", userId);
                
                await using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return Ok(new
                    {
                        currentDay = reader.GetInt32(0),
                        lastPromptDate = reader.IsDBNull(1) ? null : reader.GetDateTime(1).ToString("yyyy-MM-dd"),
                        isNewDay = reader.IsDBNull(1) || reader.GetDateTime(1).Date < DateTime.Today
                    });
                }
                
                return Ok(new { currentDay = 1, isNewDay = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting progress");
                return StatusCode(500, new { error = "Failed to get progress" });
            }
        }
    }
    
    public class DailyPromptResponse
    {
        public int DayNumber { get; set; }
        public string Title { get; set; } = "";
        public string PromptText { get; set; } = "";
        public string? Category { get; set; }
        public string? Difficulty { get; set; }
    }
}
