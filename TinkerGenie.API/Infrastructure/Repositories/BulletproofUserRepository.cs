using Npgsql;
using TinkerGenie.API.Core.Entities;
using TinkerGenie.API.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Dapper;

namespace TinkerGenie.API.Infrastructure.Repositories
{
    /// <summary>
    /// BULLETPROOF User Repository - Direct SQL, no ORM issues, maximum performance
    /// </summary>
    public class BulletproofUserRepository : IUserRepository
    {
        private readonly string _connectionString;
        private readonly ILogger<BulletproofUserRepository> _logger;

        public BulletproofUserRepository(IConfiguration configuration, ILogger<BulletproofUserRepository> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("DefaultConnection string is not configured.");
            _logger = logger;
        }

        public async Task<User?> GetByEmailAsync(string email)
        {
            const string sql = @"
                SELECT id, email, username, password_hash AS PasswordHash, first_name AS FirstName, last_name AS LastName, display_name AS DisplayName,
                       role, is_active AS IsActive, is_verified AS IsVerified, requires_password_setup, requires_password_change,
                       failed_login_attempts, account_locked_until, last_password_change, last_login,
                       business_name, business_role, external_id, google_id, microsoft_id, apple_id,
                       preferences, metadata, created_at, updated_at, created_by, updated_by,
                       migration_source, data_version
                FROM users 
                WHERE LOWER(email) = LOWER(@Email) AND is_active = true";
            
            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();
                var user = await connection.QueryFirstOrDefaultAsync<User>(sql, new { Email = email.Trim() });
                
                if (user != null)
                {
                    _logger.LogInformation("Found user: {Email} (ID: {UserId})", user.Email, user.Id);
                }
                else
                {
                    _logger.LogWarning("User not found: {Email}", email);
                }
                
                return user;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user by email: {Email}", email);
                throw;
            }
        }

        public async Task<User?> GetByIdAsync(Guid id)
        {
            const string sql = @"
                SELECT id, email, username, password_hash AS PasswordHash, first_name AS FirstName, last_name AS LastName, display_name AS DisplayName,
                       role, is_active AS IsActive, is_verified AS IsVerified, requires_password_setup, requires_password_change,
                       failed_login_attempts, account_locked_until, last_password_change, last_login,
                       business_name, business_role, external_id, google_id, microsoft_id, apple_id,
                       preferences, metadata, created_at, updated_at, created_by, updated_by,
                       migration_source, data_version
                FROM users 
                WHERE id = @Id AND is_active = true";
            
            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();
                var user = await connection.QueryFirstOrDefaultAsync<User>(sql, new { Id = id });
                
                if (user != null)
                {
                    _logger.LogInformation("Found user by ID: {UserId} ({Email})", user.Id, user.Email);
                }
                
                return user;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user by ID: {Id}", id);
                throw;
            }
        }

        public async Task<User> CreateAsync(User user)
        {
            const string sql = @"
                INSERT INTO users (
                    id, email, username, password_hash AS PasswordHash, first_name AS FirstName, last_name AS LastName,
                    role, is_active AS IsActive, is_verified AS IsVerified, requires_password_setup, requires_password_change,
                    failed_login_attempts, account_locked_until, last_password_change, last_login,
                    business_name, business_role, external_id, google_id, microsoft_id, apple_id,
                    preferences, metadata, created_at, updated_at, created_by, updated_by,
                    migration_source, data_version
                ) VALUES (
                    @Id, @Email, @Username, @PasswordHash, @FirstName, @LastName,
                    @Role, @IsActive, @IsVerified, @RequiresPasswordSetup, @RequiresPasswordChange,
                    @FailedLoginAttempts, @AccountLockedUntil, @LastPasswordChange, @LastLogin,
                    @BusinessName, @BusinessRole, @ExternalId, @GoogleId, @MicrosoftId, @AppleId,
                    @Preferences::jsonb, @Metadata::jsonb, @CreatedAt, @UpdatedAt, @CreatedBy, @UpdatedBy,
                    @MigrationSource, @DataVersion
                )
                RETURNING id, email, username, password_hash AS PasswordHash, first_name AS FirstName, last_name AS LastName, display_name AS DisplayName,
                          role, is_active AS IsActive, is_verified AS IsVerified, requires_password_setup, requires_password_change,
                          failed_login_attempts, account_locked_until, last_password_change, last_login,
                          business_name, business_role, external_id, google_id, microsoft_id, apple_id,
                          preferences, metadata, created_at, updated_at, created_by, updated_by,
                          migration_source, data_version";

            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();
                var createdUser = await connection.QuerySingleOrDefaultAsync<User>(sql, user);
                
                if (createdUser == null)
                {
                    throw new InvalidOperationException("Failed to create user, no data returned.");
                }
                
                _logger.LogInformation("User created successfully: {Email} (ID: {UserId})", createdUser.Email, createdUser.Id);
                return createdUser;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating user: {Email}", user.Email);
                throw;
            }
        }

        public async Task<User> UpdateAsync(User user)
        {
            const string sql = @"
                UPDATE users SET
                    email = @Email, username = @Username, password_hash AS PasswordHash = @PasswordHash,
                    first_name AS FirstName = @FirstName, last_name AS LastName = @LastName,
                    role = @Role, is_active AS IsActive = @IsActive, is_verified AS IsVerified = @IsVerified,
                    requires_password_setup = @RequiresPasswordSetup, requires_password_change = @RequiresPasswordChange,
                    failed_login_attempts = @FailedLoginAttempts, account_locked_until = @AccountLockedUntil,
                    last_password_change = @LastPasswordChange, last_login = @LastLogin,
                    business_name = @BusinessName, business_role = @BusinessRole,
                    external_id = @ExternalId, google_id = @GoogleId, microsoft_id = @MicrosoftId, apple_id = @AppleId,
                    preferences = @Preferences, metadata = @Metadata,
                    updated_at = CURRENT_TIMESTAMP, updated_by = @UpdatedBy,
                    data_version = data_version + 1
                WHERE id = @Id
                RETURNING id, email, username, password_hash AS PasswordHash, first_name AS FirstName, last_name AS LastName, display_name AS DisplayName,
                          role, is_active AS IsActive, is_verified AS IsVerified, requires_password_setup, requires_password_change,
                          failed_login_attempts, account_locked_until, last_password_change, last_login,
                          business_name, business_role, external_id, google_id, microsoft_id, apple_id,
                          preferences, metadata, created_at, updated_at, created_by, updated_by,
                          migration_source, data_version";

            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();
                var updatedUser = await connection.QuerySingleOrDefaultAsync<User>(sql, user);
                
                if (updatedUser == null)
                {
                    throw new InvalidOperationException($"User with ID {user.Id} not found for update.");
                }
                
                _logger.LogInformation("User updated successfully: {Email} (ID: {UserId})", updatedUser.Email, updatedUser.Id);
                return updatedUser;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating user: {Email}", user.Email);
                throw;
            }
        }

        public async Task<bool> ExistsAsync(string email)
        {
            const string sql = "SELECT COUNT(1) FROM users WHERE LOWER(email) = LOWER(@Email)";
            
            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();
                var count = await connection.ExecuteScalarAsync<int>(sql, new { Email = email.Trim() });
                return count > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if user exists: {Email}", email);
                throw;
            }
        }

        /// <summary>
        /// BULLETPROOF: Update last login timestamp
        /// </summary>
        public async Task UpdateLastLoginAsync(Guid userId)
        {
            const string sql = @"
                UPDATE users 
                SET last_login = CURRENT_TIMESTAMP, 
                    failed_login_attempts = 0,
                    updated_at = CURRENT_TIMESTAMP,
                    data_version = data_version + 1
                WHERE id = @UserId";
            
            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();
                await connection.ExecuteAsync(sql, new { UserId = userId });
                _logger.LogInformation("Updated last login for user: {UserId}", userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating last login for user: {UserId}", userId);
                throw;
            }
        }

        /// <summary>
        /// BULLETPROOF: Increment failed login attempts
        /// </summary>
        public async Task IncrementFailedLoginAsync(string email)
        {
            const string sql = @"
                UPDATE users 
                SET failed_login_attempts = failed_login_attempts + 1,
                    account_locked_until = CASE 
                        WHEN failed_login_attempts >= 4 THEN CURRENT_TIMESTAMP + INTERVAL '15 minutes'
                        ELSE account_locked_until
                    END,
                    updated_at = CURRENT_TIMESTAMP,
                    data_version = data_version + 1
                WHERE LOWER(email) = LOWER(@Email)";
            
            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();
                await connection.ExecuteAsync(sql, new { Email = email.Trim() });
                _logger.LogWarning("Incremented failed login attempts for: {Email}", email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error incrementing failed login attempts for: {Email}", email);
                throw;
            }
        }
    }
}
