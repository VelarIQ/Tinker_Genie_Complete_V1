using Microsoft.AspNetCore.Mvc;
using TinkerGenie.API.Core.Interfaces;
using TinkerGenie.API.Core.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using BCrypt.Net;
using System.ComponentModel.DataAnnotations;
using System.Data;

namespace TinkerGenie.API.Presentation.Controllers
{
    /// <summary>
    /// User Sync Controller - Handles MySQL to PostgreSQL user synchronization
    /// Designed to work with N8N workflows for automated user import
    /// </summary>
    [ApiController]
    [Route("api/user-sync")]
    public class UserSyncController : ControllerBase
    {
        private readonly IUserRepository _userRepository;
        private readonly IConfiguration _configuration;
        private readonly ILogger<UserSyncController> _logger;
        private readonly string _mysqlConnectionString;

        public UserSyncController(
            IUserRepository userRepository,
            IConfiguration configuration,
            ILogger<UserSyncController> logger)
        {
            _userRepository = userRepository;
            _configuration = configuration;
            _logger = logger;
            
            // MySQL connection string for source data
            _mysqlConnectionString = _configuration.GetConnectionString("MySqlConnection") 
                ?? throw new InvalidOperationException("MySQL connection string not configured");
        }

        private static string? GetNullableString(IDataRecord record, string column)
        {
            var ordinal = record.GetOrdinal(column);
            return record.IsDBNull(ordinal) ? null : record.GetString(ordinal);
        }

        private static DateTime GetDateTimeOrDefault(IDataRecord record, string column, DateTime? defaultValue = null)
        {
            var ordinal = record.GetOrdinal(column);
            return record.IsDBNull(ordinal) ? (defaultValue ?? DateTime.UtcNow) : record.GetDateTime(ordinal);
        }

        /// <summary>
        /// Get all users from MySQL that need to be synced to PostgreSQL
        /// </summary>
        [HttpGet("mysql-users")]
        public async Task<IActionResult> GetMySqlUsers([FromQuery] string limit = "100", [FromQuery] string offset = "0")
        {
            try
            {
                if (!int.TryParse(limit, out var limitValue) || limitValue <= 0)
                {
                    limitValue = 100;
                }

                if (!int.TryParse(offset, out var offsetValue) || offsetValue < 0)
                {
                    offsetValue = 0;
                }

                var users = new List<MySqlUserDto>();
                
                using var connection = new MySqlConnection(_mysqlConnectionString);
                await connection.OpenAsync();
                
                var sql = @"
                    SELECT id, email, username, first_name, last_name, password_hash, 
                           role, is_active, created_at, updated_at
                    FROM users 
                    WHERE email IS NOT NULL AND email != ''
                    ORDER BY created_at DESC
                    LIMIT @limit OFFSET @offset";
                
                using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@limit", limitValue);
                command.Parameters.AddWithValue("@offset", offsetValue);
                
                using var reader = await command.ExecuteReaderAsync();
                
                while (await reader.ReadAsync())
                {
                    users.Add(new MySqlUserDto
                    {
                        Id = reader.GetInt32("id"),
                        UserId = reader.GetString("user_id"),
                        Username = GetNullableString(reader, "username"),
                        FirstName = GetNullableString(reader, "first_name"),
                        LastName = GetNullableString(reader, "last_name"),
                        Email = reader.GetString(reader.GetOrdinal("email")),
                        Phone = GetNullableString(reader, "phone"),
                        CreatedAt = GetDateTimeOrDefault(reader, "created_at", DateTime.UtcNow).ToUniversalTime(),
                        UpdatedAt = GetDateTimeOrDefault(reader, "updated_at").ToUniversalTime()
                    });
                }
                
                _logger.LogInformation("Retrieved {Count} users from MySQL", users.Count);
                
                return Ok(new
                {
                    success = true,
                    users = users,
                    total = users.Count,
                    limit = limitValue,
                    offset = offsetValue
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving MySQL users");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error retrieving MySQL users",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// Sync a single user from MySQL to PostgreSQL
        /// </summary>
        [HttpPost("sync-user")]
        public async Task<IActionResult> SyncUser([FromBody] MySqlUserDto mysqlUser)
        {
            if (mysqlUser == null)
            {
                return BadRequest(new { success = false, message = "Invalid payload" });
            }

            try
            {
                if (string.IsNullOrWhiteSpace(mysqlUser.Email))
                {
                    return BadRequest(new { success = false, message = "Email is required" });
                }

                // Check if user already exists in PostgreSQL
                var existingUser = await _userRepository.GetByEmailAsync(mysqlUser.Email);
                
                if (existingUser != null)
                {
                    _logger.LogInformation("User {Email} already exists in PostgreSQL", mysqlUser.Email);
                    return Ok(new
                    {
                        success = true,
                        action = "skipped",
                        message = "User already exists",
                        userId = existingUser.Id
                    });
                }

                // Create new user in PostgreSQL
                var newUser = new User
                {
                    Id = Guid.NewGuid(),
                    Email = mysqlUser.Email.ToLower().Trim(),
                    Username = mysqlUser.Username,
                    FirstName = mysqlUser.FirstName,
                    LastName = mysqlUser.LastName,
                    Role = "user",
                    IsActive = true,
                    CreatedAt = mysqlUser.CreatedAt.ToUniversalTime(),
                    UpdatedAt = mysqlUser.UpdatedAt.ToUniversalTime(),
                    MigrationSource = "mysql",
                    ExternalId = mysqlUser.UserId
                };

                // Handle password migration
                if (string.IsNullOrWhiteSpace(mysqlUser.PasswordHash))
                {
                    newUser.RequiresPasswordSetup = true;
                    newUser.RequiresPasswordChange = true;
                }
                else if (mysqlUser.PasswordHash.StartsWith("$2") && mysqlUser.PasswordHash.Length > 50)
                {
                    newUser.PasswordHash = mysqlUser.PasswordHash;
                }
                else
                {
                    newUser.RequiresPasswordSetup = true;
                    newUser.RequiresPasswordChange = true;
                }

                var createdUser = await _userRepository.CreateAsync(newUser);
                
                _logger.LogInformation("Successfully synced user {Email} from MySQL to PostgreSQL", mysqlUser.Email);
                
                return Ok(new
                {
                    success = true,
                    action = "created",
                    message = "User successfully synced",
                    userId = createdUser.Id,
                    requiresPasswordSetup = createdUser.RequiresPasswordSetup
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing user {Email}", mysqlUser.Email);
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error syncing user",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// Bulk sync multiple users from MySQL to PostgreSQL
        /// </summary>
        [HttpPost("bulk-sync")]
        public async Task<IActionResult> BulkSyncUsers([FromBody] BulkSyncRequest request)
        {
            try
            {
                var results = new List<SyncResult>();
                
                foreach (var mysqlUser in request.Users)
                {
                    try
                    {
                        var existingUser = await _userRepository.GetByEmailAsync(mysqlUser.Email);
                        
                        if (existingUser != null)
                        {
                            results.Add(new SyncResult
                            {
                                Email = mysqlUser.Email,
                                Success = true,
                                Action = "skipped",
                                Message = "User already exists",
                                UserId = existingUser.Id
                            });
                            continue;
                        }

                        var newUser = new User
                        {
                            Id = Guid.NewGuid(),
                            Email = mysqlUser.Email.ToLower().Trim(),
                            Username = mysqlUser.Username,
                            FirstName = mysqlUser.FirstName,
                            LastName = mysqlUser.LastName,
                            Role = "user",
                            IsActive = true,
                            CreatedAt = mysqlUser.CreatedAt.ToUniversalTime(),
                            UpdatedAt = mysqlUser.UpdatedAt.ToUniversalTime(),
                            MigrationSource = "mysql",
                            ExternalId = mysqlUser.UserId
                        };

                        if (string.IsNullOrWhiteSpace(mysqlUser.PasswordHash))
                        {
                            newUser.RequiresPasswordSetup = true;
                            newUser.RequiresPasswordChange = true;
                        }
                        else if (mysqlUser.PasswordHash.StartsWith("$2") && mysqlUser.PasswordHash.Length > 50)
                        {
                            newUser.PasswordHash = mysqlUser.PasswordHash;
                        }
                        else
                        {
                            newUser.RequiresPasswordSetup = true;
                            newUser.RequiresPasswordChange = true;
                        }

                        var createdUser = await _userRepository.CreateAsync(newUser);
                        
                        results.Add(new SyncResult
                        {
                            Email = mysqlUser.Email,
                            Success = true,
                            Action = "created",
                            Message = "User successfully synced",
                            UserId = createdUser.Id
                        });
                    }
                    catch (Exception ex)
                    {
                        results.Add(new SyncResult
                        {
                            Email = mysqlUser.Email,
                            Success = false,
                            Action = "error",
                            Message = ex.Message
                        });
                    }
                }
                
                var successCount = results.Count(r => r.Success);
                var errorCount = results.Count(r => !r.Success);
                
                _logger.LogInformation("Bulk sync completed: {Success} successful, {Errors} errors", 
                    successCount, errorCount);
                
                return Ok(new
                {
                    success = true,
                    totalProcessed = results.Count,
                    successCount = successCount,
                    errorCount = errorCount,
                    results = results
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in bulk sync");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error in bulk sync",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// Get sync statistics
        /// </summary>
        [HttpGet("stats")]
        public Task<IActionResult> GetSyncStats()
        {
            try
            {
                // Count users by migration source
                const int mysqlUsers = 0;
                const int manualUsers = 0;
                const int totalUsers = 0;

                // This would need to be implemented in the repository
                // For now, return basic stats
                
                var response = new
                {
                    success = true,
                    stats = new
                    {
                        totalUsers,
                        mysqlUsers,
                        manualUsers,
                        lastSyncTime = DateTime.UtcNow
                    }
                };

                return Task.FromResult<IActionResult>(Ok(response));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting sync stats");
                var errorResponse = new
                {
                    success = false,
                    message = "Error getting sync stats",
                    error = ex.Message
                };

                return Task.FromResult<IActionResult>(StatusCode(500, errorResponse));
            }
        }
    }

    public class MySqlUserDto
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Username { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PasswordHash { get; set; }
        public string? Phone { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class BulkSyncRequest
    {
        [Required]
        public List<MySqlUserDto> Users { get; set; } = new();
    }

    public class SyncResult
    {
        public string Email { get; set; } = "";
        public bool Success { get; set; }
        public string Action { get; set; } = "";
        public string Message { get; set; } = "";
        public Guid? UserId { get; set; }
    }
}
