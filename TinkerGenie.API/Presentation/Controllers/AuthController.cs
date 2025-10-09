using Microsoft.AspNetCore.Mvc;
using TinkerGenie.API.Core.Interfaces;
using TinkerGenie.API.Presentation.Models;
using Microsoft.Extensions.Logging;

namespace TinkerGenie.API.Presentation.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthenticationService _authService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            IAuthenticationService authService,
            ILogger<AuthController> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            try
            {
                _logger.LogInformation("Login attempt - Email: '{Email}', Username: '{Username}', RememberMe: {RememberMe}", 
                    request?.Email ?? "NULL", request?.Username ?? "NULL", request?.RememberMe ?? false);
                
                if (request == null || !request.IsValid())
                {
                    return BadRequest("Email and password are required");
                }

                var result = await _authService.AuthenticateAsync(request.GetEmailOrUsername(), request.Password, request.RememberMe);

                if (!result.Success)
                {
                    if (result.RequiresPasswordSetup)
                    {
                        return Ok(new LoginResponse
                        {
                            Success = false,
                            RequiresPasswordSetup = true,
                            Message = result.ErrorMessage,
                            Email = result.User?.Email
                        });
                    }

                    return Unauthorized(new LoginResponse
                    {
                        Success = false,
                        Message = result.ErrorMessage
                    });
                }

                return Ok(new LoginResponse
                {
                    Success = true,
                    Token = result.Token,
                    RequiresPasswordChange = result.User!.RequiresPasswordChange,
                    User = new UserDto
                    {
                        Id = result.User!.Id,
                        Email = result.User.Email,
                        Username = result.User.Username,
                        FirstName = result.User.FirstName,
                        LastName = result.User.LastName,
                        Role = result.User.Role,
                        DisplayName = result.User.GetDisplayName
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Login error for: {EmailOrUsername}", request?.GetEmailOrUsername() ?? "NULL");
                return StatusCode(500, new LoginResponse
                {
                    Success = false,
                    Message = "An error occurred during login"
                });
            }
        }

        [HttpPost("logout")]
        public IActionResult Logout()
        {
            return Ok(new { success = true, message = "Logged out successfully" });
        }
    }
}
