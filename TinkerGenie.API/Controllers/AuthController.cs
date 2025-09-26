using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Npgsql;
using TinkerGenie.API.Services;
using StackExchange.Redis;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Text.Json;

namespace TinkerGenie.API.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly string _connectionString;
        private readonly ILogger<AuthController> _logger;
        private readonly IConfiguration _configuration;
        private readonly IUserDataIsolationService? _userDataIsolation;
        private readonly IConnectionMultiplexer? _redis;

        public AuthController(IConfiguration configuration, ILogger<AuthController> logger, IUserDataIsolationService? userDataIsolation = null, IConnectionMultiplexer? redis = null)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
            _configuration = configuration;
            _logger = logger;
            _userDataIsolation = userDataIsolation;
            _redis = redis;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            try
            {
                var usernameOrEmail = string.IsNullOrEmpty(request.Email) ? request.Username : request.Email;
                if (string.IsNullOrEmpty(usernameOrEmail) || string.IsNullOrEmpty(request.Password))
                {
                    return BadRequest(new { success = false, message = "Username and password are required" });
                }

                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                // Check by username/email - Join with users table to get proper name
                // First check if user exists
                bool userFound = false;
                string? existingUserId = null;
                string? existingName = null;
                string? existingEmail = null;
                string? existingHash = null;
                string? existingMetadata = null;

                await using (var cmd = new NpgsqlCommand(@"
                    SELECT 
                        ud.id::text, 
                        COALESCE(u.first_name, ud.name) as name, 
                        ud.email, 
                        ud.password_hash, 
                        ud.user_id, 
                        ud.metadata
                    FROM user_data ud
                    LEFT JOIN users u ON u.username = ud.user_id
                    WHERE LOWER(ud.user_id) = LOWER(@username) 
                       OR LOWER(ud.email) = LOWER(@username)
                       OR LOWER(ud.name) = LOWER(@username)
                    LIMIT 1", conn))
                {
                    cmd.Parameters.AddWithValue("username", usernameOrEmail);

                    await using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        userFound = true;
                        existingUserId = reader.GetString(0);
                        existingName = reader.IsDBNull(1) ? "" : reader.GetString(1);
                        existingEmail = reader.IsDBNull(2) ? "" : reader.GetString(2);
                        existingHash = reader.IsDBNull(3) ? null : reader.GetString(3);
                        existingMetadata = reader.IsDBNull(5) ? null : reader.GetString(5);
                    }
                } // Reader is closed here

                if (userFound)
                {
                    // If no password hash exists, set default password
                    if (string.IsNullOrEmpty(existingHash))
                    {
                        // Check for admin user with admin password
                        if ((usernameOrEmail.ToLower() == "admin" || usernameOrEmail.ToLower().EndsWith("@twobrain.ai")) && request.Password == "TinkerAdmin2025!")
                        {
                            // Set the admin password for this user
                            await SetPasswordForUser(existingUserId!, "TinkerAdmin2025!");
                            
                            // Ensure user exists in users table (for multi-tenant system)
                            await EnsureUserInUsersTable(existingUserId!, usernameOrEmail, existingName ?? "", existingEmail ?? "");
                            
                            // Initialize multi-tenant resources
                            await InitializeMultiTenantResources(existingUserId!);
                            
                            return await GenerateToken(existingUserId!, existingName ?? "", existingEmail ?? "");
                        }
                        // Check for regular users with default password
                        else if (request.Password == "TinkerGenie2025!")
                        {
                            // Set the default password for this user
                            await SetPasswordForUser(existingUserId!, "TinkerGenie2025!");
                            
                            // Ensure user exists in users table (for multi-tenant system)
                            await EnsureUserInUsersTable(existingUserId!, usernameOrEmail, existingName ?? "", existingEmail ?? "");
                            
                            // Initialize multi-tenant resources
                            await InitializeMultiTenantResources(existingUserId!);
                            
                            return await GenerateToken(existingUserId!, existingName ?? "", existingEmail ?? "");
                        }
                        return Unauthorized(new { success = false, message = "Invalid credentials" });
                    }

                    // Verify password
                    if (VerifyPassword(request.Password, existingHash!))
                    {
                        // Ensure user exists in users table (for multi-tenant system)
                        await EnsureUserInUsersTable(existingUserId!, usernameOrEmail, existingName ?? "", existingEmail ?? "");
                        
                        // Initialize multi-tenant resources
                        await InitializeMultiTenantResources(existingUserId!);
                        
                        return await GenerateToken(existingUserId!, existingName ?? "", existingEmail ?? "", existingMetadata);
                    }
                    
                    // Special case: if admin user tries to use admin password, reset it
                    if (usernameOrEmail.ToLower() == "admin" && request.Password == "TinkerAdmin2025!")
                    {
                        await SetPasswordForUser(existingUserId!, "TinkerAdmin2025!");
                        
                        // Ensure user exists in users table (for multi-tenant system)
                        await EnsureUserInUsersTable(existingUserId!, usernameOrEmail, existingName ?? "", existingEmail ?? "");
                        
                        // Initialize multi-tenant resources
                        await InitializeMultiTenantResources(existingUserId!);
                        
                        return await GenerateToken(existingUserId!, existingName ?? "", existingEmail ?? "");
                    }
                }

                // If no user found, create one automatically if using default password
                if (!userFound && (request.Password == "TinkerGenie2025!" || (usernameOrEmail.ToLower() == "admin" && request.Password == "TinkerAdmin2025!")))
                {
                    var newUserId = Guid.NewGuid();
                    var passwordToUse = usernameOrEmail.ToLower() == "admin" ? "TinkerAdmin2025!" : "TinkerGenie2025!";
                    
                    await using var createCmd = new NpgsqlCommand(@"
                        INSERT INTO user_data (id, user_id, name, email, current_day, password_hash)
                        VALUES (@id, @userId, @name, @email, @currentDay, @passwordHash)
                        RETURNING id::text", conn);
                    
                    createCmd.Parameters.AddWithValue("id", newUserId);
                    createCmd.Parameters.AddWithValue("userId", usernameOrEmail.ToLower());
                    // Keep original case for display name but use lowercase for username
                    string displayName = usernameOrEmail;
                    if (usernameOrEmail.ToLower() == "admin") displayName = "Administrator";
                    
                    createCmd.Parameters.AddWithValue("name", displayName);
                    createCmd.Parameters.AddWithValue("email", usernameOrEmail.Contains("@") ? usernameOrEmail : $"{usernameOrEmail}@tinkergenie.com");
                    createCmd.Parameters.AddWithValue("currentDay", 1);
                    createCmd.Parameters.AddWithValue("passwordHash", HashPassword(passwordToUse));
                    
                    var createdId = await createCmd.ExecuteScalarAsync() as string;
                    
                    if (!string.IsNullOrEmpty(createdId))
                    {
                        // Also create entry in users table with lowercase username
                        await using var createUserCmd = new NpgsqlCommand(@"
                            INSERT INTO users (id, username, email, first_name, tbb_user_id, created_at, is_active)
                            VALUES (@id, @username, @email, @firstName, 1, CURRENT_TIMESTAMP, true)
                            ON CONFLICT (id) DO NOTHING", conn);
                        
                        createUserCmd.Parameters.AddWithValue("id", newUserId);
                        createUserCmd.Parameters.AddWithValue("username", usernameOrEmail.ToLower());
                        createUserCmd.Parameters.AddWithValue("email", usernameOrEmail.Contains("@") ? usernameOrEmail : $"{usernameOrEmail}@tinkergenie.com");
                        createUserCmd.Parameters.AddWithValue("firstName", displayName.Split(' ').First());
                        
                        await createUserCmd.ExecuteNonQueryAsync();
                        
                        // Initialize all multi-tenant resources for the new user
                        await InitializeMultiTenantResources(createdId);
                        
                        return await GenerateToken(createdId, displayName, 
                            usernameOrEmail.Contains("@") ? usernameOrEmail : $"{usernameOrEmail}@tinkergenie.com");
                    }
                }

                return Unauthorized(new { success = false, message = "Invalid credentials" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Login error");
                return StatusCode(500, new { success = false, message = "An error occurred during login" });
            }
        }

        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            try
            {
                var userId = User.FindFirst("userId")?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized();
                }

                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                // Verify old password
                await using var checkCmd = new NpgsqlCommand(@"
                    SELECT password_hash FROM user_data WHERE id::text = @userId", conn);
                checkCmd.Parameters.AddWithValue("userId", userId);

                var storedHash = await checkCmd.ExecuteScalarAsync() as string;
                
                if (!string.IsNullOrEmpty(storedHash) && !VerifyPassword(request.OldPassword, storedHash))
                {
                    return BadRequest(new { success = false, message = "Current password is incorrect" });
                }

                // Update password
                await SetPasswordForUser(userId, request.NewPassword);

                return Ok(new { success = true, message = "Password changed successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Change password error");
                return StatusCode(500, new { success = false, message = "An error occurred while changing password" });
            }
        }

        private async Task SetPasswordForUser(string userId, string password)
        {
            var hashedPassword = HashPassword(password);
            
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(@"
                UPDATE user_data 
                SET password_hash = @hash,
                    metadata = COALESCE(metadata::jsonb, '{}'::jsonb) || '{""requirePasswordChange"": false}'::jsonb
                WHERE id::text = @userId", conn);
            
            cmd.Parameters.AddWithValue("hash", hashedPassword);
            cmd.Parameters.AddWithValue("userId", userId);
            
            await cmd.ExecuteNonQueryAsync();
        }

        private string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password + "TinkerGenieSalt2025"));
            return Convert.ToBase64String(hashedBytes);
        }

        private bool VerifyPassword(string password, string hash)
        {
            var passwordHash = HashPassword(password);
            return passwordHash == hash;
        }

        private async Task<IActionResult> GenerateToken(string userId, string name, string email, string? metadata = null)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes("TinkerGenieJWTSecretKey2025VeryLongAndSecure");

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim("userId", userId),
                    new Claim("name", name ?? userId),
                    new Claim("email", email ?? $"{userId}@tinkergenie.com"),
                    new Claim(ClaimTypes.Name, email ?? userId),
                    new Claim(ClaimTypes.Role, "user")
                }),
                Expires = DateTime.UtcNow.AddDays(7),
                Issuer = "TinkerGenieAPI",
                Audience = "TinkerGenieApp",
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            var tokenString = tokenHandler.WriteToken(token);

            // Check if password change is required
            bool requirePasswordChange = false;
            if (!string.IsNullOrEmpty(metadata))
            {
                try
                {
                    var metaObj = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(metadata);
                    if (metaObj != null && metaObj.ContainsKey("requirePasswordChange"))
                    {
                        requirePasswordChange = metaObj["requirePasswordChange"].ToString() == "True";
                    }
                }
                catch
                {
                    // Ignore JSON parsing errors
                }
            }

            // Check if this is a first-time user (no preferences saved yet)
            bool isFirstTime = await IsFirstTimeUser(userId);
            
            // Build user payload expected by client
            var firstName = (name ?? userId).Split(' ').First();
            var lastName = (name ?? userId).Contains(' ') ? (name ?? userId).Substring((name ?? userId).IndexOf(' ') + 1) : "";
            var userPayload = new
            {
                id = userId,
                email = email ?? $"{userId}@tinkergenie.com",
                firstName = firstName,
                lastName = lastName,
                role = "user",
                name = name ?? userId
            };

            return Ok(new
            {
                success = true,
                token = tokenString,
                accessToken = tokenString,
                user = userPayload,
                requirePasswordChange = requirePasswordChange,
                isFirstTime = isFirstTime
            });
        }

        [HttpGet("validate")]
        public IActionResult ValidateToken([FromQuery] string? token)
        {
            try
            {
                var tokenString = token;
                if (string.IsNullOrEmpty(tokenString))
                {
                    var authHeader = HttpContext.Request.Headers["Authorization"].ToString();
                    if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
                    {
                        tokenString = authHeader.Substring("Bearer ".Length);
                    }
                }
                if (string.IsNullOrEmpty(tokenString))
                {
                    return Unauthorized(new { valid = false, message = "Missing token" });
                }

                var tokenHandler = new JwtSecurityTokenHandler();
                var key = Encoding.UTF8.GetBytes("TinkerGenieJWTSecretKey2025VeryLongAndSecure");
                tokenHandler.ValidateToken(tokenString, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ClockSkew = TimeSpan.Zero
                }, out var validatedToken);

                var jwt = (JwtSecurityToken)validatedToken;
                var userId = jwt.Claims.FirstOrDefault(c => c.Type == "userId")?.Value ?? "";
                var name = jwt.Claims.FirstOrDefault(c => c.Type == "name")?.Value ?? "User";
                var email = jwt.Claims.FirstOrDefault(c => c.Type == "email")?.Value ?? "";
                var firstName = name.Split(' ').First();
                var lastName = name.Contains(' ') ? name.Substring(name.IndexOf(' ') + 1) : "";

                var userPayload = new
                {
                    id = userId,
                    email = email,
                    firstName = firstName,
                    lastName = lastName,
                    role = "user",
                    name = name
                };
                return Ok(new { valid = true, user = userPayload });
            }
            catch
            {
                return Unauthorized(new { valid = false });
            }
        }

        [HttpPost("logout")]
        public IActionResult Logout()
        {
            // Stateless JWT logout - client should discard token
            return Ok(new { success = true });
        }

        private async Task<bool> IsFirstTimeUser(string userId)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                
                // Check if user has saved preferences
                var cmd = new NpgsqlCommand(@"
                    SELECT COUNT(*) 
                    FROM user_preferences 
                    WHERE user_id = @userId::uuid", conn);
                
                cmd.Parameters.AddWithValue("userId", Guid.Parse(userId));
                
                var count = (long)(await cmd.ExecuteScalarAsync() ?? 0);
                
                // If no preferences exist, it's a first-time user
                return count == 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking first time user status");
                return false; // Default to not first-time on error
            }
        }
        
        [HttpPost("setup-admin")]
        public async Task<IActionResult> SetupAdminUser()
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                // Create or update admin user
                await using var cmd = new NpgsqlCommand(@"
                    INSERT INTO user_data (id, user_id, name, email, current_day, password_hash)
                    VALUES (@id, @userId, @name, @email, @currentDay, @passwordHash)
                    ON CONFLICT (user_id) 
                    DO UPDATE SET 
                        name = EXCLUDED.name,
                        email = EXCLUDED.email,
                        password_hash = EXCLUDED.password_hash,
                        last_active = CURRENT_TIMESTAMP
                    RETURNING id", conn);

                var adminId = Guid.NewGuid();
                var adminPasswordHash = HashPassword("TinkerAdmin2025!");

                cmd.Parameters.AddWithValue("id", adminId);
                cmd.Parameters.AddWithValue("userId", "admin");
                cmd.Parameters.AddWithValue("name", "Administrator");
                cmd.Parameters.AddWithValue("email", "admin@twobrain.ai");
                cmd.Parameters.AddWithValue("currentDay", 1);
                cmd.Parameters.AddWithValue("passwordHash", adminPasswordHash);

                var id = await cmd.ExecuteScalarAsync();

                return Ok(new
                {
                    success = true,
                    message = "Admin user setup successfully",
                    adminUserId = "admin",
                    adminPassword = "TinkerAdmin2025!",
                    id = id
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting up admin user");
                return StatusCode(500, new { success = false, message = "Failed to setup admin user" });
            }
        }

        [HttpPost("setup-test-user")]
        public async Task<IActionResult> SetupTestUser([FromBody] TestUserRequest request)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                // Create or update test user
                await using var cmd = new NpgsqlCommand(@"
                    INSERT INTO user_data (id, user_id, name, email, current_day, password_hash)
                    VALUES (@id, @userId, @name, @email, @currentDay, @passwordHash)
                    ON CONFLICT (user_id) 
                    DO UPDATE SET 
                        name = EXCLUDED.name,
                        email = EXCLUDED.email,
                        password_hash = EXCLUDED.password_hash,
                        last_active = CURRENT_TIMESTAMP
                    RETURNING id", conn);

                var userId = Guid.NewGuid();
                var passwordHash = HashPassword("TinkerGenie2025!");

                cmd.Parameters.AddWithValue("id", userId);
                cmd.Parameters.AddWithValue("userId", request.Username ?? userId.ToString());
                cmd.Parameters.AddWithValue("name", request.Name ?? "Test User");
                cmd.Parameters.AddWithValue("email", request.Email ?? $"{request.Username ?? userId.ToString()}@tinkergenie.com");
                cmd.Parameters.AddWithValue("currentDay", 1);
                cmd.Parameters.AddWithValue("passwordHash", passwordHash);

                var id = await cmd.ExecuteScalarAsync();

                return Ok(new
                {
                    success = true,
                    message = "Test user setup successfully",
                    userId = request.Username ?? userId.ToString(),
                    password = "TinkerGenie2025!",
                    id = id
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting up test user");
                return StatusCode(500, new { success = false, message = "Failed to setup test user" });
            }
        }

        [HttpPost("reset-all-users")]
        public async Task<IActionResult> ResetAllUsers()
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                // Start transaction for atomic reset
                await using var transaction = await conn.BeginTransactionAsync();

                try
                {
                    // Clear all existing users
                    await using var clearUsersCmd = new NpgsqlCommand(@"
                        DELETE FROM user_data;
                        DELETE FROM user_preferences;
                        DELETE FROM user_profiles;
                        DELETE FROM conversation_messages;
                        DELETE FROM genie_conversations;
                        DELETE FROM search_history WHERE id > 0;
                    ", conn, transaction);
                    await clearUsersCmd.ExecuteNonQueryAsync();

                    // Create fresh admin user
                    var adminId = Guid.NewGuid();
                    var adminPasswordHash = HashPassword("TinkerAdmin2025!");
                    
                    await using var adminCmd = new NpgsqlCommand(@"
                        INSERT INTO user_data (id, user_id, name, email, current_day, password_hash, created_at, last_active)
                        VALUES (@id, @userId, @name, @email, @currentDay, @passwordHash, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)", 
                        conn, transaction);
                    
                    adminCmd.Parameters.AddWithValue("id", adminId);
                    adminCmd.Parameters.AddWithValue("userId", "admin");
                    adminCmd.Parameters.AddWithValue("name", "Administrator");
                    adminCmd.Parameters.AddWithValue("email", "admin@twobrain.ai");
                    adminCmd.Parameters.AddWithValue("currentDay", 1);
                    adminCmd.Parameters.AddWithValue("passwordHash", adminPasswordHash);
                    await adminCmd.ExecuteNonQueryAsync();

                    // Create demo user
                    var demoId = Guid.NewGuid();
                    var demoPasswordHash = HashPassword("TinkerGenie2025!");
                    
                    await using var demoCmd = new NpgsqlCommand(@"
                        INSERT INTO user_data (id, user_id, name, email, current_day, password_hash, created_at, last_active)
                        VALUES (@id, @userId, @name, @email, @currentDay, @passwordHash, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)", 
                        conn, transaction);
                    
                    demoCmd.Parameters.AddWithValue("id", demoId);
                    demoCmd.Parameters.AddWithValue("userId", "demo");
                    demoCmd.Parameters.AddWithValue("name", "Demo User");
                    demoCmd.Parameters.AddWithValue("email", "demo@tinkergenie.com");
                    demoCmd.Parameters.AddWithValue("currentDay", 1);
                    demoCmd.Parameters.AddWithValue("passwordHash", demoPasswordHash);
                    await demoCmd.ExecuteNonQueryAsync();

                    // Commit transaction
                    await transaction.CommitAsync();

                    // Get count of users
                    await using var countCmd = new NpgsqlCommand("SELECT COUNT(*) FROM user_data", conn);
                    var userCount = await countCmd.ExecuteScalarAsync();

                    return Ok(new
                    {
                        success = true,
                        message = "All users reset to baseline successfully",
                        usersCreated = userCount,
                        adminUser = new { username = "admin", password = "TinkerAdmin2025!" },
                        demoUser = new { username = "demo", password = "TinkerGenie2025!" },
                        resetDate = DateTime.UtcNow
                    });
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting all users");
                return StatusCode(500, new { success = false, message = "Failed to reset users" });
            }
        }
        
        private async Task EnsureUserInUsersTable(string userId, string username, string name, string email)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                
                // Check if user exists in users table
                var checkCmd = new NpgsqlCommand(@"
                    SELECT COUNT(*) FROM users WHERE id = @userId::uuid", conn);
                checkCmd.Parameters.AddWithValue("userId", Guid.Parse(userId));
                
                var userExists = (long)(await checkCmd.ExecuteScalarAsync() ?? 0) > 0;
                
                if (!userExists)
                {
                    _logger.LogInformation("Creating missing users table entry for {Username} ({UserId})", username, userId);
                    
                    // Create user in users table
                    var createCmd = new NpgsqlCommand(@"
                        INSERT INTO users (id, username, email, first_name, tbb_user_id, created_at, is_active)
                        VALUES (@id, @username, @email, @firstName, 1, CURRENT_TIMESTAMP, true)
                        ON CONFLICT (id) DO NOTHING", conn);
                    
                    createCmd.Parameters.AddWithValue("id", Guid.Parse(userId));
                    createCmd.Parameters.AddWithValue("username", username.ToLower());
                    createCmd.Parameters.AddWithValue("email", string.IsNullOrEmpty(email) || !email.Contains("@") 
                        ? $"{username.ToLower()}@tinkergenie.com" 
                        : email);
                    createCmd.Parameters.AddWithValue("firstName", string.IsNullOrEmpty(name) ? username : name);
                    
                    await createCmd.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ensuring user exists in users table: {UserId}", userId);
                // Don't fail login if we can't create the user record
            }
        }
        
        private async Task InitializeMultiTenantResources(string userId)
        {
            try
            {
                var username = "";
                
                // Get username for this user
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                
                var getUsernameCmd = new NpgsqlCommand(@"
                    SELECT COALESCE(user_id, '') FROM user_data WHERE id = @userId::uuid
                    UNION
                    SELECT COALESCE(username, '') FROM users WHERE id = @userId::uuid
                    LIMIT 1", conn);
                getUsernameCmd.Parameters.AddWithValue("userId", Guid.Parse(userId));
                
                var result = await getUsernameCmd.ExecuteScalarAsync();
                if (result != null)
                {
                    username = result.ToString()?.ToLower() ?? userId;
                }
                
                // Create Weaviate collection for the user (tenant isolation)
                if (_userDataIsolation != null && !string.IsNullOrEmpty(username))
                {
                    try
                    {
                        _logger.LogInformation("Ensuring Weaviate collection exists for user: {Username}", username);
                        await _userDataIsolation.EnsureUserCollectionExistsAsync(username);
                    }
                    catch (Exception wex)
                    {
                        _logger.LogError(wex, "Failed to create Weaviate collection for user {Username}", username);
                    }
                }
                
                // Initialize Redis keys for multi-tenant isolation
                if (_redis != null && !string.IsNullOrEmpty(username))
                {
                    try
                    {
                        var db = _redis.GetDatabase();
                        
                        // Create user-specific Redis keys for multi-tenant isolation
                        var userKeys = new Dictionary<string, string>
                        {
                            [$"user:{username}:initialized"] = DateTime.UtcNow.ToString("O"),
                            [$"user:{username}:session_data"] = "{}",
                            [$"user:{username}:preferences"] = "{}",
                            [$"user:{username}:daily_prompt_status"] = "{}",
                            [$"user:{username}:conversation_context"] = "{}"
                        };
                        
                        // Set all user keys with appropriate expiration
                        foreach (var kvp in userKeys)
                        {
                            await db.StringSetAsync(kvp.Key, kvp.Value, TimeSpan.FromDays(365));
                        }
                        
                        _logger.LogInformation("Initialized Redis multi-tenant keys for user: {Username}", username);
                    }
                    catch (Exception rex)
                    {
                        _logger.LogError(rex, "Failed to initialize Redis multi-tenant keys for user {Username}", username);
                    }
                }
                
                // Ensure conversation record exists for foreign key requirements
                await using var conn2 = new NpgsqlConnection(_connectionString);
                await conn2.OpenAsync();
                
                var ensureConversationCmd = new NpgsqlCommand(@"
                    INSERT INTO conversations (conversation_id, user_id, created_at, last_message_at)
                    SELECT gen_random_uuid(), @userId::uuid, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                    WHERE NOT EXISTS (
                        SELECT 1 FROM conversations WHERE user_id = @userId::uuid
                    )", conn2);
                ensureConversationCmd.Parameters.AddWithValue("userId", Guid.Parse(userId));
                
                await ensureConversationCmd.ExecuteNonQueryAsync();
                
                _logger.LogInformation("Multi-tenant resources initialized for user {UserId}", userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing multi-tenant resources for user: {UserId}", userId);
                // Don't fail login if we can't initialize resources
            }
        }
        
        /// <summary>
        /// Cleans up all Redis keys for a specific user (multi-tenant cleanup)
        /// </summary>
        private async Task CleanupUserRedisKeys(string username)
        {
            if (_redis == null || string.IsNullOrEmpty(username)) return;
            
            try
            {
                var db = _redis.GetDatabase();
                var server = _redis.GetServer(_redis.GetEndPoints().First());
                
                // Get all keys for this user
                var userKeyPattern = $"user:{username.ToLower()}:*";
                var userKeys = server.Keys(pattern: userKeyPattern).ToArray();
                
                if (userKeys.Length > 0)
                {
                    await db.KeyDeleteAsync(userKeys);
                    _logger.LogInformation("Cleaned up {KeyCount} Redis keys for user: {Username}", userKeys.Length, username);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up Redis keys for user: {Username}", username);
            }
        }
        
        [HttpPost("refresh")]
        [Authorize] // Requires valid token to refresh
        public async Task<IActionResult> RefreshToken()
        {
            try
            {
                // Get current user info from token
                var userId = User.FindFirst("userId")?.Value;
                var name = User.FindFirst("name")?.Value ?? "User";
                var email = User.FindFirst("email")?.Value ?? "";
                
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new { success = false, message = "Invalid token" });
                }
                
                _logger.LogInformation("Refreshing token for user: {UserId}", userId);
                
                // Generate new token with same claims but extended expiration
                return await GenerateToken(userId, name, email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing token");
                return Unauthorized(new { success = false, message = "Token refresh failed" });
            }
        }
    }

    public class LoginRequest
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }

    public class ChangePasswordRequest
    {
        public string OldPassword { get; set; } = "";
        public string NewPassword { get; set; } = "";
    }

    public class TestUserRequest
    {
        public string? Username { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
    }
}
