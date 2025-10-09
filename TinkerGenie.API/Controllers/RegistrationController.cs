using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Npgsql;
using MySql.Data.MySqlClient;

namespace TinkerGenie.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RegistrationController : ControllerBase
    {
        private readonly ILogger<RegistrationController> _logger;
        private readonly IConfiguration _configuration;

        public RegistrationController(
            ILogger<RegistrationController> logger,
            IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        [HttpPost("check-email")]
        [AllowAnonymous]
        public async Task<IActionResult> CheckEmailEligibility([FromBody] EmailCheckRequest request)
        {
            try
            {
                var email = request.Email.ToLower().Trim();
                _logger.LogInformation("Checking email eligibility: {Email}", email);

                // Check if already registered in users table
                var alreadyRegistered = await IsEmailAlreadyRegistered(email);
                if (alreadyRegistered)
                {
                    return Ok(new { 
                        eligible = false, 
                        reason = "already_registered",
                        message = "This email is already registered. Please login instead." 
                    });
                }

                // Check PostgreSQL whitelist
                var inPostgres = await IsEmailInPostgresWhitelist(email);
                if (inPostgres)
                {
                    _logger.LogInformation("Email {Email} found in PostgreSQL whitelist", email);
                    return Ok(new { 
                        eligible = true,
                        source = "postgresql",
                        message = "Email verified! You can proceed with registration." 
                    });
                }

                // Check MySQL whitelist (optional)
                var mysqlEnabled = !string.IsNullOrEmpty(_configuration.GetConnectionString("MySQL"));
                if (mysqlEnabled)
                {
                    var inMySQL = await IsEmailInMySQLWhitelist(email);
                    if (inMySQL)
                    {
                        _logger.LogInformation("Email {Email} found in MySQL whitelist", email);
                        return Ok(new { 
                            eligible = true,
                            source = "mysql",
                            message = "Email verified! You can proceed with registration." 
                        });
                    }
                }

                // Email not in any whitelist
                _logger.LogWarning("Email {Email} not found in any whitelist", email);
                return Ok(new { 
                    eligible = false,
                    reason = "not_whitelisted",
                    message = "This email is not authorized for registration. Please contact your administrator." 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking email eligibility");
                return StatusCode(500, new { message = "An error occurred checking email eligibility." });
            }
        }

        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<IActionResult> Register([FromBody] RegistrationRequest request)
        {
            try
            {
                var email = request.Email.ToLower().Trim();
                _logger.LogInformation("Registration attempt for email: {Email}", email);

                // Validate email is whitelisted
                var alreadyRegistered = await IsEmailAlreadyRegistered(email);
                if (alreadyRegistered)
                {
                    return BadRequest(new { message = "Email already registered." });
                }

                var inPostgres = await IsEmailInPostgresWhitelist(email);
                var mysqlEnabled = !string.IsNullOrEmpty(_configuration.GetConnectionString("MySQL"));
                var inMySQL = mysqlEnabled ? await IsEmailInMySQLWhitelist(email) : false;

                if (!inPostgres && !inMySQL)
                {
                    _logger.LogWarning("Registration blocked for non-whitelisted email: {Email}", email);
                    return Unauthorized(new { message = "Email not authorized for registration." });
                }

                // Validate password strength
                if (request.Password.Length < 8)
                {
                    return BadRequest(new { message = "Password must be at least 8 characters." });
                }

                // Create user account
                var userId = Guid.NewGuid();
                var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

                var connectionString = _configuration.GetConnectionString("PostgreSQL");
                using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();

                var query = @"
                    INSERT INTO users (
                        id, email, username, password_hash, first_name, last_name,
                        role, is_active, is_verified, must_reset_password,
                        business_name, created_at, updated_at
                    ) VALUES (
                        @Id, @Email, @Username, @PasswordHash, @FirstName, @LastName,
                        @Role, true, true, false,
                        @BusinessName, @CreatedAt, @UpdatedAt
                    )";

                using var cmd = new NpgsqlCommand(query, conn);
                cmd.Parameters.AddWithValue("Id", userId);
                cmd.Parameters.AddWithValue("Email", email);
                cmd.Parameters.AddWithValue("Username", request.Username ?? email.Split('@')[0]);
                cmd.Parameters.AddWithValue("PasswordHash", passwordHash);
                cmd.Parameters.AddWithValue("FirstName", request.FirstName ?? "");
                cmd.Parameters.AddWithValue("LastName", request.LastName ?? "");
                cmd.Parameters.AddWithValue("Role", "user");
                cmd.Parameters.AddWithValue("BusinessName", request.BusinessName ?? "");
                cmd.Parameters.AddWithValue("CreatedAt", DateTime.UtcNow);
                cmd.Parameters.AddWithValue("UpdatedAt", DateTime.UtcNow);

                await cmd.ExecuteNonQueryAsync();

                _logger.LogInformation("User registered successfully: {Email}", email);

                return Ok(new { 
                    message = "Registration successful! You can now login.",
                    userId = userId.ToString(),
                    email = email
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during registration");
                return StatusCode(500, new { message = "An error occurred during registration." });
            }
        }

        private async Task<bool> IsEmailAlreadyRegistered(string email)
        {
            try
            {
                var connectionString = _configuration.GetConnectionString("PostgreSQL");
                using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();

                var query = "SELECT COUNT(*) FROM users WHERE LOWER(email) = @Email";
                using var cmd = new NpgsqlCommand(query, conn);
                cmd.Parameters.AddWithValue("Email", email.ToLower());

                var count = (long)(await cmd.ExecuteScalarAsync() ?? 0);
                return count > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if email is registered");
                return false;
            }
        }

        private async Task<bool> IsEmailInPostgresWhitelist(string email)
        {
            try
            {
                var connectionString = _configuration.GetConnectionString("PostgreSQL");
                var whitelistTable = _configuration["Registration:PostgresWhitelistTable"] ?? "user_data";
                var whitelistColumn = _configuration["Registration:PostgresWhitelistColumn"] ?? "email";

                using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();

                var query = $"SELECT COUNT(*) FROM {whitelistTable} WHERE LOWER({whitelistColumn}) = @Email";
                using var cmd = new NpgsqlCommand(query, conn);
                cmd.Parameters.AddWithValue("Email", email.ToLower());

                var count = (long)(await cmd.ExecuteScalarAsync() ?? 0);
                return count > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking PostgreSQL whitelist");
                return false;
            }
        }

        private async Task<bool> IsEmailInMySQLWhitelist(string email)
        {
            try
            {
                var connectionString = _configuration.GetConnectionString("MySQL");
                if (string.IsNullOrEmpty(connectionString))
                {
                    return false;
                }

                var whitelistTable = _configuration["Registration:MySQLWhitelistTable"] ?? "clients";
                var whitelistColumn = _configuration["Registration:MySQLWhitelistColumn"] ?? "email";

                using var conn = new MySqlConnection(connectionString);
                await conn.OpenAsync();

                var query = $"SELECT COUNT(*) FROM {whitelistTable} WHERE LOWER({whitelistColumn}) = @Email";
                using var cmd = new MySqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@Email", email.ToLower());

                var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                return count > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking MySQL whitelist");
                return false;
            }
        }
    }

    public class EmailCheckRequest
    {
        public string Email { get; set; } = string.Empty;
    }

    public class RegistrationRequest
    {
        public string Email { get; set; } = string.Empty;
        public string? Username { get; set; }
        public string Password { get; set; } = string.Empty;
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? BusinessName { get; set; }
    }
}
