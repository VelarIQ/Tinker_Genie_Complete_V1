using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Text.Json;
using Npgsql;

namespace TinkerGenie.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/analytics")]
    public class AnalyticsController : ControllerBase
    {
        private readonly string _connectionString;
        private readonly ILogger<AnalyticsController> _logger;

        public AnalyticsController(IConfiguration configuration, ILogger<AnalyticsController> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
            _logger = logger;
        }

        [HttpPost("track")]
        public async Task<IActionResult> TrackEvent([FromBody] AnalyticsEventRequest request)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                await using var cmd = new NpgsqlCommand(@"
                    INSERT INTO analytics_events 
                    (user_id, event_type, event_data, timestamp, session_id)
                    VALUES (@userId, @eventType, @eventData, CURRENT_TIMESTAMP, @sessionId)", conn);

                cmd.Parameters.AddWithValue("userId", request.UserId ?? "anonymous");
                cmd.Parameters.AddWithValue("eventType", request.EventType ?? "unknown");
                cmd.Parameters.AddWithValue("eventData", JsonSerializer.Serialize(request.EventData ?? new { }));
                cmd.Parameters.AddWithValue("sessionId", request.SessionId ?? "");

                await cmd.ExecuteNonQueryAsync();

                return Ok(new { success = true, message = "Event tracked successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error tracking analytics event");
                // Don't fail the request if analytics fails
                return Ok(new { success = true, message = "Event tracked (logged)" });
            }
        }

        [HttpGet("events/{userId}")]
        public async Task<IActionResult> GetUserEvents(string userId, [FromQuery] int limit = 50)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                await using var cmd = new NpgsqlCommand(@"
                    SELECT event_type, event_data, timestamp, session_id
                    FROM analytics_events 
                    WHERE user_id = @userId 
                    ORDER BY timestamp DESC 
                    LIMIT @limit", conn);

                cmd.Parameters.AddWithValue("userId", userId);
                cmd.Parameters.AddWithValue("limit", limit);

                var events = new List<object>();
                await using var reader = await cmd.ExecuteReaderAsync();
                
                while (await reader.ReadAsync())
                {
                    events.Add(new
                    {
                        eventType = reader.GetString(0),
                        eventData = JsonSerializer.Deserialize<object>(reader.GetString(1)),
                        timestamp = reader.GetDateTime(2),
                        sessionId = reader.GetString(3)
                    });
                }

                return Ok(new { events, count = events.Count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving analytics events for user {UserId}", userId);
                return StatusCode(500, new { message = "Error retrieving events" });
            }
        }
    }

    public class AnalyticsEventRequest
    {
        public string? UserId { get; set; }
        public string? EventType { get; set; }
        public object? EventData { get; set; }
        public string? SessionId { get; set; }
    }
}

