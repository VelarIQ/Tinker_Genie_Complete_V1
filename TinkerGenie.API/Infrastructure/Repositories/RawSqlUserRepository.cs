using System;
using System.Threading.Tasks;
using Npgsql;
using TinkerGenie.API.Core.Entities;
using TinkerGenie.API.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TinkerGenie.API.Infrastructure.Repositories
{
    /// <summary>
    /// Raw SQL User Repository - Bypasses Entity Framework column case issues
    /// </summary>
    public class RawSqlUserRepository : IUserRepository
    {
        private readonly string _connectionString;
        private readonly ILogger<RawSqlUserRepository> _logger;

        public RawSqlUserRepository(IConfiguration configuration, ILogger<RawSqlUserRepository> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? throw new InvalidOperationException("Database connection string not configured");
            _logger = logger;
        }

        public async Task<User?> GetByIdAsync(Guid id)
        {
            const string sql = @"
                SELECT id, email, username, first_name, password_hash, 
                       requires_password_setup, requires_password_change, 
                       last_password_change, role, is_active, created_at, updated_at
                FROM users 
                WHERE id = @id";

            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();
                
                using var command = new NpgsqlCommand(sql, connection);
                command.Parameters.AddWithValue("@id", id);
                
                using var reader = await command.ExecuteReaderAsync();
                
                if (await reader.ReadAsync())
                {
                    return MapUserFromReader(reader);
                }
                
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user by ID: {UserId}", id);
                throw;
            }
        }

        public async Task<User?> GetByEmailAsync(string email)
        {
            const string sql = @"
                SELECT id, email, username, first_name, password_hash, 
                       requires_password_setup, requires_password_change, 
                       last_password_change, role, is_active, created_at, updated_at
                FROM users 
                WHERE email = @email";

            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();
                
                using var command = new NpgsqlCommand(sql, connection);
                command.Parameters.AddWithValue("@email", email.ToLower().Trim());
                
                using var reader = await command.ExecuteReaderAsync();
                
                if (await reader.ReadAsync())
                {
                    return MapUserFromReader(reader);
                }
                
                _logger.LogWarning("User not found with email: {Email}", email);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user by email: {Email}", email);
                throw;
            }
        }

        public async Task<User> CreateAsync(User user)
        {
            const string sql = @"
                INSERT INTO users (id, email, username, first_name, password_hash, 
                                       requires_password_setup, requires_password_change, 
                                       last_password_change, role, is_active, created_at, updated_at)
                VALUES (@id, @email, @username, @first_name, @password_hash, 
                        @requires_password_setup, @requires_password_change, 
                        @last_password_change, @role, @is_active, @created_at, @updated_at)
                RETURNING id, email, username, first_name, password_hash, 
                          requires_password_setup, requires_password_change, 
                          last_password_change, role, is_active, created_at, updated_at";

            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();
                
                using var command = new NpgsqlCommand(sql, connection);
                AddUserParameters(command, user);
                
                using var reader = await command.ExecuteReaderAsync();
                
                if (await reader.ReadAsync())
                {
                    var createdUser = MapUserFromReader(reader);
                    _logger.LogInformation("User created successfully: {Email}", user.Email);
                    return createdUser;
                }
                
                throw new InvalidOperationException("Failed to create user");
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
                UPDATE users 
                SET email = @email, username = @username, first_name = @first_name, 
                    password_hash = @password_hash, requires_password_setup = @requires_password_setup, 
                    requires_password_change = @requires_password_change, 
                    last_password_change = @last_password_change, role = @role, 
                    is_active = @is_active, updated_at = @updated_at
                WHERE id = @id
                RETURNING id, email, username, first_name, password_hash, 
                          requires_password_setup, requires_password_change, 
                          last_password_change, role, is_active, created_at, updated_at";

            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();
                
                using var command = new NpgsqlCommand(sql, connection);
                AddUserParameters(command, user);
                
                using var reader = await command.ExecuteReaderAsync();
                
                if (await reader.ReadAsync())
                {
                    var updatedUser = MapUserFromReader(reader);
                    _logger.LogInformation("User updated successfully: {Email}", user.Email);
                    return updatedUser;
                }
                
                throw new InvalidOperationException($"User with ID {user.Id} not found for update");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating user: {Email}", user.Email);
                throw;
            }
        }

        public async Task<bool> ExistsAsync(string email)
        {
            const string sql = "SELECT COUNT(1) FROM users_clean WHERE email = @email";

            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();
                
                using var command = new NpgsqlCommand(sql, connection);
                command.Parameters.AddWithValue("@email", email.ToLower().Trim());
                
                var count = await command.ExecuteScalarAsync();
                return Convert.ToInt32(count) > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if user exists: {Email}", email);
                throw;
            }
        }

        private static User MapUserFromReader(NpgsqlDataReader reader)
        {
            return new User
            {
                Id = reader.GetGuid(0), // id
                Email = reader.GetString(1), // email
                Username = reader.IsDBNull(2) ? null : reader.GetString(2), // username
                FirstName = reader.IsDBNull(3) ? null : reader.GetString(3), // first_name
                PasswordHash = reader.IsDBNull(4) ? null : reader.GetString(4), // password_hash
                RequiresPasswordSetup = reader.GetBoolean(5), // requires_password_setup
                RequiresPasswordChange = reader.GetBoolean(6), // requires_password_change
                LastPasswordChange = reader.IsDBNull(7) ? null : reader.GetDateTime(7), // last_password_change
                Role = reader.GetString(8), // role
                IsActive = reader.GetBoolean(9), // is_active
                CreatedAt = reader.GetDateTime(10), // created_at
                UpdatedAt = reader.GetDateTime(11) // updated_at
            };
        }

        private static void AddUserParameters(NpgsqlCommand command, User user)
        {
            command.Parameters.AddWithValue("@id", user.Id);
            command.Parameters.AddWithValue("@email", user.Email.ToLower().Trim());
            command.Parameters.AddWithValue("@username", (object?)user.Username ?? DBNull.Value);
            command.Parameters.AddWithValue("@first_name", (object?)user.FirstName ?? DBNull.Value);
            command.Parameters.AddWithValue("@password_hash", (object?)user.PasswordHash ?? DBNull.Value);
            command.Parameters.AddWithValue("@requires_password_setup", user.RequiresPasswordSetup);
            command.Parameters.AddWithValue("@requires_password_change", user.RequiresPasswordChange);
            command.Parameters.AddWithValue("@last_password_change", (object?)user.LastPasswordChange ?? DBNull.Value);
            command.Parameters.AddWithValue("@role", user.Role);
            command.Parameters.AddWithValue("@is_active", user.IsActive);
            command.Parameters.AddWithValue("@created_at", user.CreatedAt);
            command.Parameters.AddWithValue("@updated_at", DateTime.UtcNow);
        }
    }
}
