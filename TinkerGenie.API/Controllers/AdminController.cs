using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using StackExchange.Redis;
using System.Text.Json;
using TinkerGenie.API.Services;

namespace TinkerGenie.API.Controllers
{
    [ApiController]
    [Route("api/admin")]
    [Authorize] // simple auth; additionally require header key below
    public class AdminController : ControllerBase
    {
        private readonly ILogger<AdminController> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;
        private readonly IConnectionMultiplexer? _redis;

        public AdminController(
            ILogger<AdminController> logger,
            IConfiguration configuration,
            IConnectionMultiplexer? redis = null)
        {
            _logger = logger;
            _configuration = configuration;
            _redis = redis;
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
        }

        // POST /api/admin/purge-all-chats  (requires header X-Admin-Key matching Admin:PurgeKey)
        [HttpPost("purge-all-chats")]
        public async Task<IActionResult> PurgeAllChats()
        {
            var headerKey = Request.Headers["X-Admin-Key"].FirstOrDefault();
            var configKey = _configuration["Admin:PurgeKey"];
            if (string.IsNullOrEmpty(configKey) || headerKey != configKey)
            {
                return Unauthorized(new { error = "Invalid admin key" });
            }

            var report = new Dictionary<string, object?>();

            // 1) Postgres: delete chat data
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                var cmd = new NpgsqlCommand(@"
                    -- thread model
                    DELETE FROM thread_messages;
                    DELETE FROM conversation_threads;
                    -- legacy tables if present
                    DO $$ BEGIN
                      IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name='conversation_messages') THEN
                        DELETE FROM conversation_messages;
                      END IF;
                    END $$;", conn);
                var affected = await cmd.ExecuteNonQueryAsync();
                report["postgres"] = new { ok = true, affected };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Postgres purge failed");
                report["postgres"] = new { ok = false, error = ex.Message };
            }

            // 2) Redis: delete session and thread keys
            try
            {
                if (_redis != null)
                {
                    var db = _redis.GetDatabase();
                    var server = _redis.GetServers().FirstOrDefault();
                    if (server != null)
                    {
                        int deleted = 0;
                        foreach (var pattern in new[] { "session:*", "thread:*", "history:*", "chat:*" })
                        {
                            foreach (var key in server.Keys(pattern: pattern))
                            {
                                if (await db.KeyDeleteAsync(key)) deleted++;
                            }
                        }
                        report["redis"] = new { ok = true, deleted };
                    }
                    else
                    {
                        report["redis"] = new { ok = false, error = "No Redis server found" };
                    }
                }
                else
                {
                    report["redis"] = new { ok = false, error = "Redis not configured" };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis purge failed");
                report["redis"] = new { ok = false, error = ex.Message };
            }

            // 3) Weaviate: delete all user collections (optional configuration)
            try
            {
                var weaviateUrl = _configuration["Weaviate:Url"];
                if (!string.IsNullOrWhiteSpace(weaviateUrl))
                {
                    var client = new HttpClient();
                    var classesResponse = await client.GetAsync($"{weaviateUrl.TrimEnd('/')}/v1/schema");
                    if (!classesResponse.IsSuccessStatusCode)
                    {
                        report["weaviate"] = new { ok = false, error = $"Failed to fetch schema ({classesResponse.StatusCode})" };
                    }
                    else
                    {
                        var schemaJson = await classesResponse.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(schemaJson);
                        var classes = doc.RootElement.GetProperty("classes");
                        var failed = new List<string>();
                        foreach (var c in classes.EnumerateArray())
                        {
                            var className = c.GetProperty("class").GetString();
                            if (string.IsNullOrWhiteSpace(className)) continue;

                            var deleteResp = await client.DeleteAsync($"{weaviateUrl.TrimEnd('/')}/v1/schema/{className}");
                            if (!deleteResp.IsSuccessStatusCode)
                            {
                                failed.Add(className);
                            }
                        }

                        report["weaviate"] = new { ok = failed.Count == 0, failed };
                    }
                }
                else
                {
                    report["weaviate"] = new { ok = false, error = "Weaviate not configured" };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Weaviate purge failed");
                report["weaviate"] = new { ok = false, error = ex.Message };
            }

            return Ok(new { success = true, report });
        }

        // POST /api/admin/purge-user/{userId}
        [HttpPost("purge-user/{userId}")]
        public async Task<IActionResult> PurgeUserChats(string userId)
        {
            var headerKey = Request.Headers["X-Admin-Key"].FirstOrDefault();
            var configKey = _configuration["Admin:PurgeKey"];
            if (string.IsNullOrEmpty(configKey) || headerKey != configKey)
            {
                return Unauthorized(new { error = "Invalid admin key" });
            }

            var report = new Dictionary<string, object?>();

            Guid? userGuid = null;
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                var findCmd = new NpgsqlCommand(@"
                    SELECT id FROM user_data 
                    WHERE id::text = $1 OR email = $1 OR user_id = $1 
                    LIMIT 1", conn);
                findCmd.Parameters.AddWithValue(userId);
                var result = await findCmd.ExecuteScalarAsync();
                if (result is Guid g) userGuid = g;
                if (userGuid == null) return NotFound(new { error = "User not found" });

                // Collect thread ids for user
                var threadIds = new List<Guid>();
                var getThreads = new NpgsqlCommand(@"
                    SELECT thread_id FROM conversation_threads WHERE user_id = $1", conn);
                getThreads.Parameters.AddWithValue(userGuid.Value);
                await using (var r = await getThreads.ExecuteReaderAsync())
                {
                    while (await r.ReadAsync()) threadIds.Add(r.GetGuid(0));
                }

                // Delete thread messages per thread
                int tmDeleted = 0;
                foreach (var tid in threadIds)
                {
                    var delTm = new NpgsqlCommand(@"DELETE FROM thread_messages WHERE thread_id = $1", conn);
                    delTm.Parameters.AddWithValue(tid);
                    tmDeleted += await delTm.ExecuteNonQueryAsync();
                }

                // Delete threads
                var delThreads = new NpgsqlCommand(@"DELETE FROM conversation_threads WHERE user_id = $1", conn);
                delThreads.Parameters.AddWithValue(userGuid.Value);
                var threadsDeleted = await delThreads.ExecuteNonQueryAsync();

                // Legacy messages table
                var delLegacy = new NpgsqlCommand(@"
                    DO $$ BEGIN
                      IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name='conversation_messages') THEN
                        DELETE FROM conversation_messages WHERE user_id = $1; 
                      END IF; 
                    END $$;", conn);
                delLegacy.Parameters.AddWithValue(userGuid.Value);
                await delLegacy.ExecuteNonQueryAsync();

                report["postgres"] = new { ok = true, threadsDeleted, tmDeleted };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Postgres user purge failed");
                report["postgres"] = new { ok = false, error = ex.Message };
            }

            // Redis cleanup for specific user
            try
            {
                if (_redis != null && userGuid.HasValue)
                {
                    var db = _redis.GetDatabase();
                    var server = _redis.GetServers().FirstOrDefault();
                    if (server != null)
                    {
                        int deleted = 0;
                        foreach (var pattern in new[] { $"session:{userGuid.Value}:*", $"thread:{userGuid.Value}:*", $"history:{userGuid.Value}:*" })
                        {
                            foreach (var key in server.Keys(pattern: pattern))
                            {
                                if (await db.KeyDeleteAsync(key)) deleted++;
                            }
                        }
                        report["redis"] = new { ok = true, deleted };
                    }
                    else
                    {
                        report["redis"] = new { ok = false, error = "No Redis server found" };
                    }
                }
                else
                {
                    report["redis"] = new { ok = false, error = "Redis not configured" };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis user purge failed");
                report["redis"] = new { ok = false, error = ex.Message };
            }

            return Ok(new { success = true, report });
        }
    }
}


