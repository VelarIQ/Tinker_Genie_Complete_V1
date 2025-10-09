using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Npgsql;
using BCrypt.Net;

namespace TinkerGenie.API.Services
{
    public interface IAuthenticationTraceService
    {
        Task<AuthenticationTraceResult> TraceLoginProcess(string username, string password);
        Task<bool> MigrateUserPasswordToBcrypt(Guid userId, string currentPassword);
    }

    public class AuthenticationTraceService : IAuthenticationTraceService
    {
        private readonly ILogger<AuthenticationTraceService> _logger;
        private readonly string _connectionString;

        public AuthenticationTraceService(
            ILogger<AuthenticationTraceService> logger,
            IConfiguration configuration)
        {
            _logger = logger;
            var connection = configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connection))
            {
                throw new InvalidOperationException("Database connection string is not configured");
            }

            _connectionString = connection;
        }

        public async Task<AuthenticationTraceResult> TraceLoginProcess(string username, string password)
        {
            var result = new AuthenticationTraceResult();

            try 
            {
                // Step 1: Validate input
                result.InputValidation = ValidateInput(username, password);
                if (!result.InputValidation.IsValid)
                {
                    _logger.LogWarning("Input validation failed for username: {Username}", username);
                    return result;
                }

                // Step 2: Database User Lookup
                result.UserLookup = await LookupUser(username);
                if (!result.UserLookup.UserFound)
                {
                    _logger.LogWarning("User not found: {Username}", username);
                    return result;
                }

                // Step 3: Password Verification
                var storedHashValue = result.UserLookup.StoredPasswordHash;
                if (string.IsNullOrEmpty(storedHashValue))
                {
                    _logger.LogWarning("Stored password hash empty for user {Username}", username);
                    result.PasswordVerification = new PasswordVerificationResult { IsValid = false };
                    return result;
                }

                var passwordVerification = await VerifyPassword(storedHashValue, password);
                if (passwordVerification is null)
                {
                    _logger.LogWarning("Password verification returned null for user {Username}", username);
                    result.PasswordVerification = new PasswordVerificationResult { IsValid = false };
                    return result;
                }

                result.PasswordVerification = passwordVerification;

                // Log detailed trace
                _logger.LogInformation(
                    "Authentication Trace: Username={Username}, " +
                    "UserFound={UserFound}, " +
                    "PasswordVerified={PasswordVerified}", 
                    username, 
                    result.UserLookup.UserFound, 
                    result.PasswordVerification.IsValid
                );

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Comprehensive authentication trace failed");
                result.ErrorTrace = new ErrorTraceInfo
                {
                    ExceptionType = ex.GetType().Name,
                    Message = ex.Message,
                    StackTrace = ex.StackTrace ?? string.Empty
                };
                return result;
            }
        }

        public async Task<bool> MigrateUserPasswordToBcrypt(Guid userId, string currentPassword)
        {
            try 
            {
                // Lookup current password hash
                var currentHash = await GetCurrentPasswordHash(userId);
                if (string.IsNullOrEmpty(currentHash))
                {
                    _logger.LogWarning("Password migration failed: no stored hash for user {UserId}", userId);
                    return false;
                }

                // Verify current password
                var passwordVerification = await VerifyPassword(currentHash, currentPassword);
                if (!passwordVerification.IsValid)
                {
                    _logger.LogWarning("Password migration failed: Invalid current password for user {UserId}", userId);
                    return false;
                }

                // Generate new BCrypt hash
                var newBcryptHash = BCrypt.Net.BCrypt.HashPassword(currentPassword);

                // Update password in database
                await UpdateUserPasswordHash(userId, newBcryptHash);

                _logger.LogInformation("Successfully migrated user {UserId} password to BCrypt", userId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Password migration failed for user {UserId}", userId);
                return false;
            }
        }

        private async Task<string?> GetCurrentPasswordHash(Guid userId)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(
                "SELECT password_hash FROM users WHERE id = @userId", 
                conn
            );
            cmd.Parameters.AddWithValue("userId", userId);

            var scalar = await cmd.ExecuteScalarAsync();
            return scalar as string;
        }

        private async Task UpdateUserPasswordHash(Guid userId, string newPasswordHash)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(
                "UPDATE users SET password_hash = @newHash WHERE id = @userId", 
                conn
            );
            cmd.Parameters.AddWithValue("userId", userId);
            cmd.Parameters.AddWithValue("newHash", newPasswordHash);

            await cmd.ExecuteNonQueryAsync();
        }

        private InputValidationResult ValidateInput(string username, string password)
        {
            return new InputValidationResult
            {
                IsValid = !string.IsNullOrWhiteSpace(username) && 
                          !string.IsNullOrWhiteSpace(password) &&
                          password.Length >= 8, // Minimum password length
                Username = username,
                PasswordLength = password?.Length ?? 0
            };
        }

        private async Task<UserLookupResult> LookupUser(string username)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(
                "SELECT id, email, password_hash, is_active, " +
                "COALESCE(password_hash, '') = '' as requires_password_setup " +
                "FROM users " +
                "WHERE email = @username OR username = @username", 
                conn
            );
            cmd.Parameters.AddWithValue("username", username);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return new UserLookupResult { UserFound = false };
            }

            return new UserLookupResult
            {
                UserFound = true,
                UserId = reader.GetGuid(0),
                Email = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                StoredPasswordHash = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                IsActive = reader.GetBoolean(3),
                RequiresPasswordSetup = reader.GetBoolean(4)
            };
        }

        private Task<PasswordVerificationResult> VerifyPassword(
            string storedHash, 
            string providedPassword)
        {
            var result = new PasswordVerificationResult();

            try 
            {
                // BCrypt verification
                if (string.IsNullOrEmpty(storedHash))
                {
                    result.IsValid = false;
                    return Task.FromResult(result);
                }

                result.BcryptVerification = BCrypt.Net.BCrypt.Verify(providedPassword, storedHash);
                result.IsValid = result.BcryptVerification;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Password verification failed");
                result.IsValid = false;
            }

            return Task.FromResult(result);
        }
    }

    public class AuthenticationTraceResult
    {
        public InputValidationResult InputValidation { get; set; } = new();
        public UserLookupResult UserLookup { get; set; } = new();
        public PasswordVerificationResult PasswordVerification { get; set; } = new();
        public ErrorTraceInfo? ErrorTrace { get; set; }
    }

    public class InputValidationResult
    {
        public bool IsValid { get; set; }
        public string Username { get; set; } = string.Empty;
        public int PasswordLength { get; set; }
    }

    public class UserLookupResult
    {
        public bool UserFound { get; set; }
        public Guid UserId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string StoredPasswordHash { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool RequiresPasswordSetup { get; set; }
    }

    public class PasswordVerificationResult
    {
        public bool IsValid { get; set; }
        public bool BcryptVerification { get; set; }
    }

    public class ErrorTraceInfo
    {
        public string ExceptionType { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string StackTrace { get; set; } = string.Empty;
    }
}
