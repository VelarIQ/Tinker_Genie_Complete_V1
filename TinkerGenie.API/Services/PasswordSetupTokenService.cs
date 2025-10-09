using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using StackExchange.Redis;
using TinkerGenie.API.Utilities;

namespace TinkerGenie.API.Services
{
    public interface IPasswordSetupTokenService
    {
        Task<string> GenerateTokenAsync(Guid userId, string email);
        Task<(bool IsValid, Guid UserId, string Email)> ValidateTokenAsync(string token);
        Task InvalidateTokenAsync(string token);
    }

    public class PasswordSetupTokenService : IPasswordSetupTokenService
    {
        private readonly ILogger<PasswordSetupTokenService> _logger;
        private readonly IConnectionMultiplexer _redis;
        private readonly IDatabase _database;
        private const int TokenExpirationHours = 24;

        public PasswordSetupTokenService(
            ILogger<PasswordSetupTokenService> logger,
            IConnectionMultiplexer redis)
        {
            _logger = logger;
            _redis = redis;
            _database = redis.GetDatabase();
        }

        public async Task<string> GenerateTokenAsync(Guid userId, string email)
        {
            try
            {
                // Generate a secure random token
                var tokenBytes = new byte[32];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(tokenBytes);
                }
                
                var token = Convert.ToBase64String(tokenBytes)
                    .Replace("+", "-")
                    .Replace("/", "_")
                    .Replace("=", "");

                // Store token data in Redis with expiration
                var tokenData = new
                {
                    UserId = userId.ToString(),
                    Email = email,
                    CreatedAt = DateTime.UtcNow.ToString("O")
                };

                var key = $"password_setup_token:{token}";
                await _database.StringSetAsync(key, 
                    System.Text.Json.JsonSerializer.Serialize(tokenData), 
                    TimeSpan.FromHours(TokenExpirationHours));

                _logger.LogInformation("Generated password setup token for user {UserId}", userId);
                return token;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate password setup token for user {UserId}", userId);
                throw;
            }
        }

        public async Task<(bool IsValid, Guid UserId, string Email)> ValidateTokenAsync(string token)
        {
            try
            {
                var key = $"password_setup_token:{token}";
                var tokenDataJson = await _database.StringGetAsync(key);

                if (!tokenDataJson.HasValue)
                {
                    _logger.LogWarning("Invalid or expired password setup token: {Token}", token);
                    return (false, Guid.Empty, string.Empty);
                }

                var tokenData = JsonSerializationHelper.Deserialize<Dictionary<string, string>>(tokenDataJson!);
                
                if (tokenData == null || 
                    !tokenData.ContainsKey("UserId") || 
                    !tokenData.ContainsKey("Email"))
                {
                    _logger.LogWarning("Malformed token data for token: {Token}", token);
                    return (false, Guid.Empty, string.Empty);
                }

                if (!Guid.TryParse(tokenData["UserId"], out var userId))
                {
                    _logger.LogWarning("Invalid UserId in token: {Token}", token);
                    return (false, Guid.Empty, string.Empty);
                }

                var email = tokenData["Email"];
                _logger.LogInformation("Successfully validated password setup token for user {UserId}", userId);
                return (true, userId, email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating password setup token: {Token}", token);
                return (false, Guid.Empty, string.Empty);
            }
        }

        public async Task InvalidateTokenAsync(string token)
        {
            try
            {
                var key = $"password_setup_token:{token}";
                await _database.KeyDeleteAsync(key);
                _logger.LogInformation("Invalidated password setup token: {Token}", token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error invalidating password setup token: {Token}", token);
            }
        }
    }
}
