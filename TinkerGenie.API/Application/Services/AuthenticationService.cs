using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using TinkerGenie.API.Core.Entities;
using TinkerGenie.API.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using BCrypt.Net;

namespace TinkerGenie.API.Application.Services
{
    public class AuthenticationService : IAuthenticationService
    {
        private readonly IUserRepository _userRepository;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthenticationService> _logger;

        public AuthenticationService(
            IUserRepository userRepository,
            IConfiguration configuration,
            ILogger<AuthenticationService> logger)
        {
            _userRepository = userRepository;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<AuthenticationResult> AuthenticateAsync(string email, string password)
        {
            return await AuthenticateAsync(email, password, false);
        }

        public async Task<AuthenticationResult> AuthenticateAsync(string email, string password, bool rememberMe)
        {
            try
            {
                _logger.LogInformation("Authentication attempt for: {Email}, RememberMe: {RememberMe}", email, rememberMe);

                if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                {
                    return new AuthenticationResult 
                    { 
                        Success = false, 
                        ErrorMessage = "Email and password are required" 
                    };
                }

                var user = await _userRepository.GetByEmailAsync(email);
                if (user == null)
                {
                    _logger.LogWarning("User not found: {Email}", email);
                    return new AuthenticationResult 
                    { 
                        Success = false, 
                        ErrorMessage = "Invalid credentials" 
                    };
                }

                if (!user.HasPassword)
                {
                    _logger.LogInformation("User requires password setup: {Email}", email);
                    return new AuthenticationResult 
                    { 
                        Success = false, 
                        RequiresPasswordSetup = true,
                        ErrorMessage = "Account requires password setup",
                        User = user
                    };
                }

                if (!VerifyPassword(password, user.PasswordHash!))
                {
                    _logger.LogWarning("Invalid password for: {Email}", email);
                    return new AuthenticationResult 
                    { 
                        Success = false, 
                        ErrorMessage = "Invalid credentials" 
                    };
                }

                var token = GenerateJwtToken(user, rememberMe);
                _logger.LogInformation("Successful authentication for: {Email}", email);
                return new AuthenticationResult { Success = true, Token = token, User = user };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Authentication error for: {Email}", email);
                return new AuthenticationResult { Success = false, ErrorMessage = "Authentication failed" };
            }
        }

        public string GenerateJwtToken(User user)
        {
            return GenerateJwtToken(user, false);
        }

        public string GenerateJwtToken(User user, bool rememberMe)
        {
            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT Key not configured")));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            // FIXED: Use unique claim types to avoid duplicates
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), // USER ID AS GUID
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Name, user.GetDisplayName),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim("user_id", user.Id.ToString()), // BACKUP USER ID CLAIM
                new Claim("email", user.Email), // BACKUP EMAIL CLAIM
                new Claim("first_name", user.FirstName ?? ""),
                new Claim("display_name", user.GetDisplayName)
            };

            var expires = rememberMe ? DateTime.Now.AddDays(30) : DateTime.Now.AddHours(8);

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: expires,
                signingCredentials: credentials);

            var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
            _logger.LogInformation("Generated JWT token for user: {UserId} ({Email})", user.Id, user.Email);
            return tokenString;
        }

        public bool VerifyPassword(string password, string hash)
        {
            try
            {
                return BCrypt.Net.BCrypt.Verify(password, hash);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Password verification error");
                return false;
            }
        }

        public string HashPassword(string password)
        {
            try
            {
                return BCrypt.Net.BCrypt.HashPassword(password);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Password hashing error");
                throw;
            }
        }
    }
}
