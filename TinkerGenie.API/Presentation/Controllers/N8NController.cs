using Microsoft.AspNetCore.Mvc;
using TinkerGenie.API.Core.Interfaces;
using TinkerGenie.API.Core.Entities;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;

namespace TinkerGenie.API.Presentation.Controllers
{
    /// <summary>
    /// N8N Integration Controller - Simple endpoints for N8N workflows
    /// Fixes the MySQL to PostgreSQL user import issues
    /// </summary>
    [ApiController]
    [Route("api/n8n")]
    public class N8NController : ControllerBase
    {
        private readonly IUserRepository _userRepository;
        private readonly ILogger<N8NController> _logger;

        public N8NController(
            IUserRepository userRepository,
            ILogger<N8NController> logger)
        {
            _userRepository = userRepository;
            _logger = logger;
        }

        /// <summary>
        /// Create or update a user from N8N workflow
        /// Handles the data type mismatches that N8N was experiencing
        /// </summary>
        [HttpPost("sync-user")]
        public async Task<IActionResult> SyncUser([FromBody] N8NUserRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Email))
                {
                    return BadRequest(new { success = false, message = "Email is required" });
                }

                // Check if user already exists
                var existingUser = await _userRepository.GetByEmailAsync(request.Email);
                
                if (existingUser != null)
                {
                    _logger.LogInformation("User {Email} already exists, skipping", request.Email);
                    return Ok(new
                    {
                        success = true,
                        action = "skipped",
                        message = "User already exists",
                        userId = existingUser.Id.ToString(), // Convert to string for N8N
                        email = existingUser.Email
                    });
                }

                // Create new user
                var newUser = new User
                {
                    Id = Guid.NewGuid(),
                    Email = request.Email.ToLower().Trim(),
                    Username = request.Username,
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    Role = request.Role ?? "user",
                    IsActive = request.IsActive ?? true,
                    MigrationSource = "n8n_mysql",
                    RequiresPasswordSetup = true, // All N8N imported users need password setup
                    RequiresPasswordChange = false,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                // Handle password if provided
                if (!string.IsNullOrWhiteSpace(request.PasswordHash))
                {
                    // If it looks like a BCrypt hash, use it
                    if (request.PasswordHash.StartsWith("$2") && request.PasswordHash.Length > 50)
                    {
                        newUser.PasswordHash = request.PasswordHash;
                        newUser.RequiresPasswordSetup = false;
                    }
                    // Otherwise, user needs to set up password
                }

                var createdUser = await _userRepository.CreateAsync(newUser);
                
                _logger.LogInformation("Successfully created user {Email} from N8N", request.Email);
                
                return Ok(new
                {
                    success = true,
                    action = "created",
                    message = "User successfully created",
                    userId = createdUser.Id.ToString(), // Convert to string for N8N
                    email = createdUser.Email,
                    requiresPasswordSetup = createdUser.RequiresPasswordSetup
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing user {Email} from N8N", request.Email);
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error syncing user",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// Bulk sync multiple users from N8N
        /// </summary>
        [HttpPost("bulk-sync")]
        public async Task<IActionResult> BulkSync([FromBody] N8NBulkRequest request)
        {
            try
            {
                var results = new List<N8NSyncResult>();
                
                foreach (var userRequest in request.Users)
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(userRequest.Email))
                        {
                            results.Add(new N8NSyncResult
                            {
                                Email = userRequest.Email ?? "",
                                Success = false,
                                Action = "error",
                                Message = "Email is required"
                            });
                            continue;
                        }

                        var existingUser = await _userRepository.GetByEmailAsync(userRequest.Email);
                        
                        if (existingUser != null)
                        {
                            results.Add(new N8NSyncResult
                            {
                                Email = userRequest.Email,
                                Success = true,
                                Action = "skipped",
                                Message = "User already exists",
                                UserId = existingUser.Id.ToString()
                            });
                            continue;
                        }

                        var newUser = new User
                        {
                            Id = Guid.NewGuid(),
                            Email = userRequest.Email.ToLower().Trim(),
                            Username = userRequest.Username,
                            FirstName = userRequest.FirstName,
                            LastName = userRequest.LastName,
                            Role = userRequest.Role ?? "user",
                            IsActive = userRequest.IsActive ?? true,
                            MigrationSource = "n8n_mysql",
                            RequiresPasswordSetup = string.IsNullOrWhiteSpace(userRequest.PasswordHash),
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };

                        if (!string.IsNullOrWhiteSpace(userRequest.PasswordHash) && 
                            userRequest.PasswordHash.StartsWith("$2") && 
                            userRequest.PasswordHash.Length > 50)
                        {
                            newUser.PasswordHash = userRequest.PasswordHash;
                            newUser.RequiresPasswordSetup = false;
                        }

                        var createdUser = await _userRepository.CreateAsync(newUser);
                        
                        results.Add(new N8NSyncResult
                        {
                            Email = userRequest.Email,
                            Success = true,
                            Action = "created",
                            Message = "User successfully created",
                            UserId = createdUser.Id.ToString()
                        });
                    }
                    catch (Exception ex)
                    {
                        results.Add(new N8NSyncResult
                        {
                            Email = userRequest.Email ?? "",
                            Success = false,
                            Action = "error",
                            Message = ex.Message
                        });
                    }
                }
                
                var successCount = results.Count(r => r.Success);
                var errorCount = results.Count(r => !r.Success);
                
                _logger.LogInformation("N8N bulk sync completed: {Success} successful, {Errors} errors", 
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
                _logger.LogError(ex, "Error in N8N bulk sync");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error in bulk sync",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// Health check for N8N workflows
        /// </summary>
        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new
            {
                success = true,
                message = "N8N integration healthy",
                timestamp = DateTime.UtcNow,
                endpoints = new[]
                {
                    "/api/n8n/sync-user",
                    "/api/n8n/bulk-sync",
                    "/api/n8n/health"
                }
            });
        }
    }

    public class N8NUserRequest
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = "";
        
        public string? Username { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PasswordHash { get; set; }
        public string? Role { get; set; }
        public bool? IsActive { get; set; }
    }

    public class N8NBulkRequest
    {
        [Required]
        public List<N8NUserRequest> Users { get; set; } = new();
    }

    public class N8NSyncResult
    {
        public string Email { get; set; } = "";
        public bool Success { get; set; }
        public string Action { get; set; } = "";
        public string Message { get; set; } = "";
        public string? UserId { get; set; }
    }
}
