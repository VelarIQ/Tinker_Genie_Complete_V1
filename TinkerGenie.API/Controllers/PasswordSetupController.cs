using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TinkerGenie.API.Data;
using TinkerGenie.API.Models;
using TinkerGenie.API.Services;
using BCrypt.Net;

namespace TinkerGenie.API.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class PasswordSetupController : ControllerBase
    {
        private readonly ILogger<PasswordSetupController> _logger;
        private readonly TinkerGenieContext _context;
        private readonly IEmailService _emailService;
        private readonly IPasswordSetupTokenService _tokenService;

        public PasswordSetupController(
            ILogger<PasswordSetupController> logger,
            TinkerGenieContext context,
            IEmailService emailService,
            IPasswordSetupTokenService tokenService)
        {
            _logger = logger;
            _context = context;
            _emailService = emailService;
            _tokenService = tokenService;
        }

        [HttpPost("request-password-setup")]
        public async Task<IActionResult> RequestPasswordSetup([FromBody] PasswordSetupRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Email))
                {
                    return BadRequest(new { message = "Email is required" });
                }

                // Find user by email
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email == request.Email);

                if (user == null)
                {
                    // Don't reveal if user exists or not for security
                    return Ok(new { message = "If an account with this email exists, you will receive setup instructions." });
                }

                // Check if user needs password setup
                if (!string.IsNullOrEmpty(user.PasswordHash))
                {
                    return BadRequest(new { message = "This account already has a password set up. Use the forgot password option instead." });
                }

                // Generate setup token
                var token = await _tokenService.GenerateTokenAsync(user.Id, user.Email);

                // Send setup email
                var emailSent = await _emailService.SendPasswordSetupEmailAsync(
                    user.Email, 
                    user.FirstName ?? "User", 
                    token);

                if (!emailSent)
                {
                    _logger.LogError("Failed to send password setup email to {Email}", user.Email);
                    return StatusCode(500, new { message = "Failed to send setup email. Please try again." });
                }

                // Mark user as requiring password setup
                user.RequiresPasswordSetup = true;
                user.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                return Ok(new { 
                    message = "Password setup instructions have been sent to your email.",
                    email = user.Email
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing password setup request for {Email}", request.Email);
                return StatusCode(500, new { message = "An error occurred processing your request." });
            }
        }

        [HttpPost("setup-password")]
        public async Task<IActionResult> SetupPassword([FromBody] SetupPasswordRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Token) || 
                    string.IsNullOrWhiteSpace(request.Password))
                {
                    return BadRequest(new { message = "Token and password are required" });
                }

                if (request.Password.Length < 8)
                {
                    return BadRequest(new { message = "Password must be at least 8 characters long" });
                }

                // Validate token
                var (isValid, userId, email) = await _tokenService.ValidateTokenAsync(request.Token);
                if (!isValid)
                {
                    return BadRequest(new { message = "Invalid or expired setup token" });
                }

                // Find user
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return BadRequest(new { message = "User not found" });
                }

                // Check if password is already set
                if (!string.IsNullOrEmpty(user.PasswordHash))
                {
                    return BadRequest(new { message = "Password is already set for this account" });
                }

                // Hash and set password
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
                user.RequiresPasswordSetup = false;
                user.RequiresPasswordChange = false;
                user.LastPasswordChange = DateTime.UtcNow;
                user.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                // Invalidate the token
                await _tokenService.InvalidateTokenAsync(request.Token);

                _logger.LogInformation("Password successfully set up for user {UserId}", userId);

                return Ok(new { 
                    message = "Password set up successfully! You can now log in.",
                    email = user.Email
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting up password with token {Token}", request.Token);
                return StatusCode(500, new { message = "An error occurred setting up your password." });
            }
        }

        [HttpGet("validate-setup-token")]
        public async Task<IActionResult> ValidateSetupToken([FromQuery] string token)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    return BadRequest(new { message = "Token is required" });
                }

                var (isValid, userId, email) = await _tokenService.ValidateTokenAsync(token);
                
                if (!isValid)
                {
                    return BadRequest(new { message = "Invalid or expired token", valid = false });
                }

                // Check if user still needs password setup
                var user = await _context.Users.FindAsync(userId);
                if (user == null || !string.IsNullOrEmpty(user.PasswordHash))
                {
                    return BadRequest(new { 
                        message = "This setup link is no longer valid", 
                        valid = false 
                    });
                }

                return Ok(new { 
                    valid = true,
                    email = email,
                    firstName = user.FirstName ?? "User"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating setup token {Token}", token);
                return StatusCode(500, new { message = "Error validating token" });
            }
        }
    }

    public class PasswordSetupRequest
    {
        public string Email { get; set; } = "";
    }

    public class SetupPasswordRequest
    {
        public string Token { get; set; } = "";
        public string Password { get; set; } = "";
    }
}
