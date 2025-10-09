using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using TinkerGenie.API.Core.Interfaces;
using TinkerGenie.API.Core.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System;
using System.Threading.Tasks;

namespace TinkerGenie.API.Presentation.Controllers
{
    [ApiController]
    [Route("api/user")]
    [Authorize]
    public class BulletproofUserController : ControllerBase
    {
        private readonly ILogger<BulletproofUserController> _logger;
        private readonly IUserRepository _userRepository;

        public BulletproofUserController(ILogger<BulletproofUserController> logger, IUserRepository userRepository)
        {
            _logger = logger;
            _userRepository = userRepository;
        }

        [HttpGet("me")]
        [HttpGet("current-session")]
        public async Task<IActionResult> GetCurrentSession()
        {
            // FIXED: Use the backup user_id claim that we added to JWT
            var userIdClaim = User.FindFirst("user_id")?.Value ?? 
                             User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            
            _logger.LogInformation("GetCurrentSession: Looking for user_id claim. Found: {UserIdClaim}", userIdClaim);
            
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                _logger.LogWarning("GetCurrentSession: User ID not found or invalid. Claims: {Claims}", 
                    string.Join(", ", User.Claims.Select(c => $"{c.Type}={c.Value}")));
                return Unauthorized("User ID not found in token.");
            }

            try
            {
                var user = await _userRepository.GetByIdAsync(userId);
                if (user == null)
                {
                    _logger.LogWarning("GetCurrentSession: User not found for ID: {UserId}", userId);
                    return NotFound("User not found.");
                }

                _logger.LogInformation("GetCurrentSession: Successfully found user: {Email} (ID: {UserId})", user.Email, user.Id);

                return Ok(new
                {
                    user.Id,
                    user.Email,
                    user.Username,
                    user.FirstName,
                    user.LastName,
                    user.Role,
                    user.IsActive,
                    user.CreatedAt,
                    user.DisplayName,
                    onboardingCompleted = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting current session for user ID: {UserId}", userIdClaim);
                return StatusCode(500, new { message = "An error occurred while retrieving session data." });
            }
        }
    }
}
