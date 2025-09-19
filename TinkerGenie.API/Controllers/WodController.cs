using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace TinkerGenie.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WodController : ControllerBase
    {
        private readonly string _connectionString;
        
        public WodController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
        }
        
        [HttpGet("daily/{day}")]
        public async Task<IActionResult> GetDailyWod(int day)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                
                await using var cmd = new NpgsqlCommand(@"
                    SELECT content, metadata 
                    FROM baseline_content 
                    WHERE metadata->>'day' = @day::text
                    LIMIT 1", conn);
                
                cmd.Parameters.AddWithValue("day", day);
                
                await using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return Ok(new
                    {
                        success = true,
                        wod = reader.GetString(0),
                        day = day
                    });
                }
                
                // Fallback WOD if not in database
                var fallbackWods = new[]
                {
                    "Call three past members. Ask why they left. Action: Fix the top reason.",
                    "Review your prices. Action: Raise one service by 10% today.",
                    "Audit your coaches. Action: Fire your weakest or hire their replacement.",
                    "Check your ARM. Action: Create one upsell to increase it by $20.",
                    "Count no-shows this week. Action: Call each one personally."
                };
                
                return Ok(new
                {
                    success = true,
                    wod = fallbackWods[day % fallbackWods.Length],
                    day = day
                });
            }
            catch
            {
                return Ok(new
                {
                    success = false,
                    wod = "Identify your biggest constraint. Action: Spend 30 minutes fixing it.",
                    day = day
                });
            }
        }
        
        [HttpPost("import")]
        public async Task<IActionResult> ImportContent([FromBody] BaselineContent content)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            
            await using var cmd = new NpgsqlCommand(@"
                INSERT INTO baseline_content (content, metadata)
                VALUES (@content, @metadata::jsonb)
                ON CONFLICT DO NOTHING", conn);
            
            cmd.Parameters.AddWithValue("content", content.Content);
            cmd.Parameters.AddWithValue("metadata", System.Text.Json.JsonSerializer.Serialize(content.Metadata));
            
            await cmd.ExecuteNonQueryAsync();
            
            return Ok(new { success = true });
        }
    }
    
    public class BaselineContent
    {
        public string Content { get; set; } = "";
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
}
