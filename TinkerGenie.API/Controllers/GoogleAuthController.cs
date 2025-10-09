using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text;
using TinkerGenie.API.Data;
using TinkerGenie.API.Services;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace TinkerGenie.API.Controllers
{
    [ApiController]
    [Route("api/auth/google")]
    public class GoogleAuthController : ControllerBase
    {
        private readonly ILogger<GoogleAuthController> _logger;
        private readonly TinkerGenieContext _context;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;

        // Google OAuth configuration - use environment variables
        private readonly string GoogleClientId;
        private readonly string GoogleClientSecret;
        private const string GoogleTokenUri = "https://oauth2.googleapis.com/token";
        private const string GoogleUserInfoUri = "https://www.googleapis.com/oauth2/v2/userinfo";

        public GoogleAuthController(
            ILogger<GoogleAuthController> logger,
            TinkerGenieContext context,
            IConfiguration configuration,
            HttpClient httpClient)
        {
            _logger = logger;
            _context = context;
            _configuration = configuration;
            _httpClient = httpClient;
            GoogleClientId = configuration["Google:ClientId"] ?? "";
            GoogleClientSecret = configuration["Google:ClientSecret"] ?? "";
        }
        
        [HttpPost("callback")]
        [HttpGet("callback")]
        public async Task<IActionResult> GoogleCallback([FromQuery] string? code, [FromQuery] string? state, [FromBody] GoogleCallbackRequest? request = null)
        {
            try
            {
                var authCode = code ?? request?.Code;
                
                if (string.IsNullOrEmpty(authCode))
                {
                    _logger.LogWarning("Google OAuth callback received without authorization code");
                    return BadRequest(new { message = "Authorization code is required" });
                }

                _logger.LogInformation("Processing Google OAuth callback with code: {CodePrefix}...", authCode.Substring(0, Math.Min(10, authCode.Length)));

                // Step 1: Exchange authorization code for access token
                var tokenResponse = await ExchangeCodeForToken(authCode);
                if (tokenResponse == null)
                {
                    return BadRequest(new { message = "Failed to exchange authorization code for token" });
                }

                // Step 2: Get user info from Google
                var userInfo = await GetGoogleUserInfo(tokenResponse.AccessToken);
                if (userInfo == null)
                {
                    return BadRequest(new { message = "Failed to retrieve user information from Google" });
                }

                // Step 3: Check if user exists in PostgreSQL database
                var existingUser = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email.ToLower() == userInfo.Email.ToLower());

                if (existingUser == null)
                {
                    _logger.LogWarning("Google SSO attempted for non-existent user: {Email}", userInfo.Email);
                    return Unauthorized(new 
                    { 
                        message = "User not found in system. Please contact administrator to create your account.",
                        email = userInfo.Email,
                        error = "USER_NOT_FOUND"
                    });
                }

                // Step 4: Generate JWT token for existing user
                var jwtToken = GenerateJwtToken(existingUser);

                _logger.LogInformation("Google SSO successful for user: {Email}", userInfo.Email);
                
                return Ok(new
                {
                    token = jwtToken,
                    userId = existingUser.Id,
                    email = existingUser.Email,
                    firstName = existingUser.FirstName,
                    lastName = existingUser.LastName,
                    message = "Google SSO login successful",
                    expiresIn = "2 years",
                    provider = "google"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Google OAuth callback");
                return StatusCode(500, new { message = "Error processing Google authentication" });
            }
        }

        private async Task<GoogleTokenResponse?> ExchangeCodeForToken(string authCode)
        {
            try
            {
                var tokenRequest = new Dictionary<string, string>
                {
                    ["client_id"] = GoogleClientId,
                    ["client_secret"] = GoogleClientSecret,
                    ["code"] = authCode,
                    ["grant_type"] = "authorization_code",
                    ["redirect_uri"] = "https://tinker.twobrain.ai/api/auth/google/callback"
                };

                var content = new FormUrlEncodedContent(tokenRequest);
                var response = await _httpClient.PostAsync(GoogleTokenUri, content);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Google token exchange failed: {StatusCode} - {Content}", response.StatusCode, errorContent);
                    return null;
                }

                var jsonResponse = await response.Content.ReadAsStringAsync();
                var tokenResponse = JsonSerializer.Deserialize<GoogleTokenResponse>(jsonResponse, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                });

                return tokenResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during Google token exchange");
                return null;
            }
        }

        private async Task<GoogleUserInfo?> GetGoogleUserInfo(string accessToken)
        {
            try
            {
                _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
                
                var response = await _httpClient.GetAsync(GoogleUserInfoUri);
                
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Google user info request failed: {StatusCode}", response.StatusCode);
                    return null;
                }

                var jsonResponse = await response.Content.ReadAsStringAsync();
                var userInfo = JsonSerializer.Deserialize<GoogleUserInfo>(jsonResponse, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                });

                return userInfo;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during Google user info retrieval");
                return null;
            }
        }

        private string GenerateJwtToken(dynamic user)
        {
            var jwtKey = "TinkerGenieJWTSecretKey2025VeryLongAndSecure";
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim("userId", user.Id.ToString()),
                new Claim("email", user.Email),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.GivenName, user.FirstName ?? "User"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var token = new JwtSecurityToken(
                issuer: "TinkerGenieAPI",
                audience: "TinkerGenieApp",
                claims: claims,
                expires: DateTime.UtcNow.AddYears(2), // 2-year expiration
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        [HttpOptions("callback")]
        public IActionResult OptionsCallback()
        {
            return Ok();
        }
    }

    public class GoogleCallbackRequest
    {
        public string? Code { get; set; }
        public string? State { get; set; }
    }

    public class GoogleTokenResponse
    {
        public string AccessToken { get; set; } = "";
        public string TokenType { get; set; } = "";
        public int ExpiresIn { get; set; }
        public string? RefreshToken { get; set; }
        public string? Scope { get; set; }
    }

    public class GoogleUserInfo
    {
        public string Id { get; set; } = "";
        public string Email { get; set; } = "";
        public bool VerifiedEmail { get; set; }
        public string Name { get; set; } = "";
        public string GivenName { get; set; } = "";
        public string FamilyName { get; set; } = "";
        public string Picture { get; set; } = "";
        public string Locale { get; set; } = "";
    }
}