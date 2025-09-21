using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Npgsql;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace TinkerGenie.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UserController : ControllerBase
    {
        private readonly string _connectionString;
        private readonly ILogger<UserController> _logger;
        
        public UserController(IConfiguration configuration, ILogger<UserController> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
            _logger = logger;
        }

        // Compatibility: GET /api/user/current-session
        [HttpGet("current-session")]
        [Authorize]
        public Task<IActionResult> GetCurrentSession()
        {
            var userId = User.FindFirst("userId")?.Value ?? User.FindFirst("sub")?.Value ?? string.Empty;
            return GetSessionState(userId);
        }
        
        [HttpGet("me")]
        [Authorize]
        public async Task<IActionResult> GetCurrentUser()
        {
            try
            {
                // Get userId from JWT token claims
                var userId = User.FindFirst("userId")?.Value ?? User.FindFirst("sub")?.Value;
                var email = User.FindFirst("email")?.Value ?? User.FindFirst("preferred_username")?.Value;
                var name = User.FindFirst("name")?.Value ?? User.FindFirst("given_name")?.Value;
                
                if (string.IsNullOrEmpty(userId))
                {
                    _logger.LogWarning("No userId found in token claims");
                    return Unauthorized(new { error = "No user identifier found in token" });
                }
                
                // Get additional user data from database if available
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                
                await using var cmd = new NpgsqlCommand(@"
                    SELECT name, business_name, email, current_day, preferences
                    FROM user_data 
                    WHERE user_id = @userId OR email = @email
                    LIMIT 1", conn);
                
                cmd.Parameters.AddWithValue("userId", userId);
                cmd.Parameters.AddWithValue("email", (object?)email ?? DBNull.Value);
                
                await using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return Ok(new
                    {
                        userId = userId,
                        name = reader.IsDBNull(0) ? name : reader.GetString(0),
                        businessName = reader.IsDBNull(1) ? null : reader.GetString(1),
                        email = reader.IsDBNull(2) ? email : reader.GetString(2),
                        currentDay = reader.IsDBNull(3) ? 1 : reader.GetInt32(3),
                        preferences = reader.IsDBNull(4) ? null : reader.GetString(4),
                        source = "database"
                    });
                }
                
                // Return basic info from token if no database record
                return Ok(new
                {
                    userId = userId,
                    name = name,
                    email = email,
                    source = "token"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting current user");
                return StatusCode(500, new { error = "Failed to get user information" });
            }
        }
        
        [HttpGet("{userId}")]
        public async Task<IActionResult> GetUser(string userId)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                
                await using var cmd = new NpgsqlCommand(@"
                    SELECT name, business_name, email, current_day, preferences, metadata
                    FROM user_data 
                    WHERE user_id = @userId", conn);
                
                cmd.Parameters.AddWithValue("userId", userId);
                
                await using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return Ok(new
                    {
                        name = reader.IsDBNull(0) ? "Leader" : reader.GetString(0),
                        businessName = reader.IsDBNull(1) ? "Your Business" : reader.GetString(1),
                        email = reader.IsDBNull(2) ? null : reader.GetString(2),
                        currentDay = reader.IsDBNull(3) ? 1 : reader.GetInt32(3),
                        preferences = reader.IsDBNull(4) ? null : reader.GetString(4),
                        metadata = reader.IsDBNull(5) ? null : reader.GetString(5)
                    });
                }
                
                return NotFound();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user {UserId}", userId);
                return NotFound();
            }
        }
        
        [HttpPost]
        public async Task<IActionResult> CreateUser([FromBody] UserData userData)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                
                // Generate a UUID if not provided
                var userId = userData.UserId ?? Guid.NewGuid().ToString();
                
                await using var cmd = new NpgsqlCommand(@"
                    INSERT INTO user_data (id, user_id, name, business_name, email, current_day, password_hash)
                    VALUES (@id, @userId, @name, @businessName, @email, @currentDay, @passwordHash)
                    ON CONFLICT (user_id) 
                    DO UPDATE SET 
                        name = EXCLUDED.name,
                        business_name = EXCLUDED.business_name,
                        email = EXCLUDED.email,
                        last_active = CURRENT_TIMESTAMP,
                        password_hash = COALESCE(EXCLUDED.password_hash, user_data.password_hash)
                    RETURNING id", conn);
                
                var id = Guid.NewGuid();
                var passwordHash = HashPassword("TinkerGenie2025!"); // Set default password
                
                cmd.Parameters.AddWithValue("id", id);
                cmd.Parameters.AddWithValue("userId", userId);
                cmd.Parameters.AddWithValue("name", (object?)userData.Name ?? DBNull.Value);
                cmd.Parameters.AddWithValue("businessName", (object?)userData.BusinessName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("email", (object?)userData.Email ?? DBNull.Value);
                cmd.Parameters.AddWithValue("currentDay", userData.CurrentDay);
                cmd.Parameters.AddWithValue("passwordHash", passwordHash);
                
                var returnedId = await cmd.ExecuteScalarAsync();
                
                return Ok(new { 
                    id = returnedId, 
                    userId = userId,
                    message = "User created successfully with default password 'TinkerGenie2025!'"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating user");
                return BadRequest(new { error = "Failed to create user" });
            }
        }
        
        [HttpGet("{userId}/session")]
        [Authorize]
        public async Task<IActionResult> GetSessionState(string userId)
        {
            try
            {
                // Verify user can access their own session
                var tokenUserId = User.FindFirst("userId")?.Value ?? User.FindFirst("sub")?.Value;
                if (tokenUserId != userId && !User.IsInRole("admin"))
                {
                    return Forbid("You can only access your own session state");
                }
                
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                
                // Get user's current state
                await using var cmd = new NpgsqlCommand(@"
                    SELECT current_day, preferences, 
                           (SELECT COUNT(*) FROM conversation_messages WHERE user_id = @userGuid) as message_count,
                           (SELECT MAX(created_at) FROM conversation_messages WHERE user_id = @userGuid) as last_message
                    FROM user_data 
                    WHERE user_id = @userId", conn);
                
                var userGuid = Guid.TryParse(userId, out var uid) ? uid : Guid.Empty;
                cmd.Parameters.AddWithValue("userId", userId);
                cmd.Parameters.AddWithValue("userGuid", userGuid);
                
                await using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return Ok(new
                    {
                        currentDay = reader.IsDBNull(0) ? 1 : reader.GetInt32(0),
                        preferences = reader.IsDBNull(1) ? null : reader.GetString(1),
                        messageCount = reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
                        lastMessageTime = reader.IsDBNull(3) ? null : reader.GetDateTime(3).ToString("O"),
                        userId = userId
                    });
                }
                
                return Ok(new
                {
                    currentDay = 1,
                    messageCount = 0,
                    userId = userId,
                    isNewUser = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting session state for user {UserId}", userId);
                return StatusCode(500, new { error = "Failed to get session state" });
            }
        }
        
        [HttpPost("{userId}/progress")]
        public async Task<IActionResult> UpdateProgress(string userId, [FromBody] ProgressUpdate update)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                
                // Try to update by any identifier available: id (UUID), user_id (username), or email
                // Using id::text comparison avoids casting errors when userId is not a UUID
                await using var cmd = new NpgsqlCommand(@"
                    UPDATE user_data 
                    SET current_day = @currentDay, 
                        last_active = CURRENT_TIMESTAMP
                    WHERE id::text = @idText
                       OR user_id = @userId
                       OR email = @userEmail", conn);
                
                var email = User.FindFirst("email")?.Value ?? string.Empty;
                
                cmd.Parameters.AddWithValue("currentDay", update.CurrentDay);
                cmd.Parameters.AddWithValue("idText", userId);
                cmd.Parameters.AddWithValue("userId", userId);
                cmd.Parameters.AddWithValue("userEmail", (object?)email ?? DBNull.Value);
                
                var rowsAffected = await cmd.ExecuteNonQueryAsync();
                
                if (rowsAffected > 0)
                {
                    return Ok(new { success = true });
                }
                
                return NotFound();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating progress for user {UserId}", userId);
                return BadRequest();
            }
        }

        // Compatibility: POST /api/user/day-progress
        [HttpPost("day-progress")]
        [Authorize]
        public Task<IActionResult> UpdateDayProgress([FromBody] ProgressUpdate update)
        {
            var userId = User.FindFirst("userId")?.Value ?? User.FindFirst("sub")?.Value ?? string.Empty;
            return UpdateProgress(userId, update);
        }

        private string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password + "TinkerGenieSalt2025"));
            return Convert.ToBase64String(hashedBytes);
        }
    }
    
    public class UserData
    {
        public string? UserId { get; set; }
        public string? Name { get; set; }
        public string? BusinessName { get; set; }
        public string? Email { get; set; }
        public int CurrentDay { get; set; } = 1;
    }
    
    public class ProgressUpdate
    {
        public int CurrentDay { get; set; }
    }
}
