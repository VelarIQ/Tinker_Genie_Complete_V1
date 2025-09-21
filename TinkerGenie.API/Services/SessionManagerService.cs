using System.Text.Json;
using StackExchange.Redis;
using Microsoft.Extensions.Logging;

namespace TinkerGenie.API.Services
{
    public interface ISessionManagerService
    {
        Task<UserSession> GetOrCreateSession(string userId);
        Task<ConversationThread> GetActiveThread(string userId, ConversationType type);
        Task<ConversationThread> CreateThread(string userId, ConversationType type, string? title = null);
        Task SwitchContext(string userId, string threadId);
        Task<List<ConversationThread>> GetUserThreads(string userId, int limit = 10);
        Task UpdateThreadMessages(string threadId, string message, string response);
        Task<bool> HasActiveSession(string userId);
        Task ClearSession(string userId);
    }

    public class SessionManagerService : ISessionManagerService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<SessionManagerService> _logger;
        private readonly IConversationService _conversationService;
        private readonly string _connectionString;

        public SessionManagerService(
            IConnectionMultiplexer redis,
            ILogger<SessionManagerService> logger,
            IConversationService conversationService,
            IConfiguration configuration)
        {
            _redis = redis;
            _logger = logger;
            _conversationService = conversationService;
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
        }

        public async Task<UserSession> GetOrCreateSession(string userId)
        {
            var db = _redis.GetDatabase();
            var sessionKey = $"session:{userId}:active";
            
            // Check for existing session
            var sessionJson = await db.StringGetAsync(sessionKey);
            if (sessionJson.HasValue)
            {
                var existingSession = JsonSerializer.Deserialize<UserSession>(sessionJson!);
                
                // Check if it's a new day - reset daily prompt context if needed
                if (existingSession != null && existingSession.LastActive.Date < DateTime.UtcNow.Date)
                {
                    // New day - clear daily prompt thread but keep others
                    existingSession.ActiveThreads.Remove(ConversationType.DAILY_PROMPT);
                    existingSession.LastActive = DateTime.UtcNow;
                    await SaveSession(userId, existingSession);
                }
                
                return existingSession!;
            }

            // Create new session
            var newSession = new UserSession
            {
                SessionId = Guid.NewGuid().ToString(),
                UserId = userId,
                StartTime = DateTime.UtcNow,
                LastActive = DateTime.UtcNow,
                ActiveThreads = new Dictionary<ConversationType, string>(),
                CurrentContext = ConversationType.DAILY_PROMPT
            };

            await SaveSession(userId, newSession);
            return newSession;
        }

        public async Task<ConversationThread> GetActiveThread(string userId, ConversationType type)
        {
            var session = await GetOrCreateSession(userId);
            
            if (session.ActiveThreads.TryGetValue(type, out var threadId))
            {
                var db = _redis.GetDatabase();
                var threadJson = await db.StringGetAsync($"thread:{threadId}");
                if (threadJson.HasValue)
                {
                    return JsonSerializer.Deserialize<ConversationThread>(threadJson!)!;
                }
            }

            // No active thread - create one
            return await CreateThread(userId, type);
        }

        public async Task<ConversationThread> CreateThread(string userId, ConversationType type, string? title = null)
        {
            var thread = new ConversationThread
            {
                ThreadId = Guid.NewGuid().ToString(),
                UserId = userId,
                Type = type,
                Title = title ?? GenerateThreadTitle(type),
                StartTime = DateTime.UtcNow,
                LastActive = DateTime.UtcNow,
                Messages = new List<ThreadMessage>(),
                Status = ThreadStatus.Active,
                Metadata = new Dictionary<string, object>()
            };

            // Special handling for daily prompts
            if (type == ConversationType.DAILY_PROMPT)
            {
                var currentDay = await GetUserCurrentDay(userId);
                thread.Metadata["dayNumber"] = currentDay;
                thread.Title = $"Day {currentDay} Reflection";
            }

            // Save thread to Redis
            var db = _redis.GetDatabase();
            var threadJson = JsonSerializer.Serialize(thread);
            await db.StringSetAsync($"thread:{thread.ThreadId}", threadJson, TimeSpan.FromDays(30));

            // Update session with new thread
            var session = await GetOrCreateSession(userId);
            session.ActiveThreads[type] = thread.ThreadId;
            session.CurrentContext = type;
            await SaveSession(userId, session);

            // Also persist to PostgreSQL for history
            await PersistThreadToDatabase(thread);

            return thread;
        }

        public async Task SwitchContext(string userId, string threadId)
        {
            var db = _redis.GetDatabase();
            var threadJson = await db.StringGetAsync($"thread:{threadId}");
            
            if (threadJson.HasValue)
            {
                var thread = JsonSerializer.Deserialize<ConversationThread>(threadJson!);
                if (thread != null)
                {
                    var session = await GetOrCreateSession(userId);
                    session.CurrentContext = thread.Type;
                    session.ActiveThreads[thread.Type] = threadId;
                    await SaveSession(userId, session);
                }
            }
        }

        public async Task<List<ConversationThread>> GetUserThreads(string userId, int limit = 10)
        {
            var threads = new List<ConversationThread>();
            
            // Get from Redis first (recent/active)
            var db = _redis.GetDatabase();
            var session = await GetOrCreateSession(userId);
            
            foreach (var threadId in session.ActiveThreads.Values)
            {
                var threadJson = await db.StringGetAsync($"thread:{threadId}");
                if (threadJson.HasValue)
                {
                    threads.Add(JsonSerializer.Deserialize<ConversationThread>(threadJson!)!);
                }
            }

            // Get historical from PostgreSQL if needed
            if (threads.Count < limit)
            {
                var historicalThreads = await GetHistoricalThreads(userId, limit - threads.Count);
                threads.AddRange(historicalThreads);
            }

            return threads.OrderByDescending(t => t.LastActive).Take(limit).ToList();
        }

        public async Task UpdateThreadMessages(string threadId, string message, string response)
        {
            var db = _redis.GetDatabase();
            var threadJson = await db.StringGetAsync($"thread:{threadId}");
            
            if (threadJson.HasValue)
            {
                var thread = JsonSerializer.Deserialize<ConversationThread>(threadJson!);
                if (thread != null)
                {
                    // Add user message
                    thread.Messages.Add(new ThreadMessage
                    {
                        Role = "user",
                        Content = message,
                        Timestamp = DateTime.UtcNow
                    });

                    // Add assistant response
                    thread.Messages.Add(new ThreadMessage
                    {
                        Role = "assistant",
                        Content = response,
                        Timestamp = DateTime.UtcNow
                    });

                    thread.LastActive = DateTime.UtcNow;

                    // Update in Redis
                    var updatedJson = JsonSerializer.Serialize(thread);
                    await db.StringSetAsync($"thread:{threadId}", updatedJson, TimeSpan.FromDays(30));

                    // Async persist to database
                    _ = Task.Run(() => PersistThreadToDatabase(thread));
                }
            }
        }

        public async Task<bool> HasActiveSession(string userId)
        {
            var db = _redis.GetDatabase();
            return await db.KeyExistsAsync($"session:{userId}:active");
        }

        public async Task ClearSession(string userId)
        {
            var db = _redis.GetDatabase();
            
            // Get session to clean up threads
            var sessionJson = await db.StringGetAsync($"session:{userId}:active");
            if (sessionJson.HasValue)
            {
                var session = JsonSerializer.Deserialize<UserSession>(sessionJson!);
                if (session != null)
                {
                    // Mark threads as completed
                    foreach (var threadId in session.ActiveThreads.Values)
                    {
                        var threadKey = $"thread:{threadId}";
                        var threadJson = await db.StringGetAsync(threadKey);
                        if (threadJson.HasValue)
                        {
                            var thread = JsonSerializer.Deserialize<ConversationThread>(threadJson!);
                            if (thread != null)
                            {
                                thread.Status = ThreadStatus.Completed;
                                await PersistThreadToDatabase(thread);
                            }
                        }
                        await db.KeyDeleteAsync(threadKey);
                    }
                }
            }
            
            await db.KeyDeleteAsync($"session:{userId}:active");
        }

        private async Task SaveSession(string userId, UserSession session)
        {
            var db = _redis.GetDatabase();
            var sessionJson = JsonSerializer.Serialize(session);
            await db.StringSetAsync($"session:{userId}:active", sessionJson, TimeSpan.FromHours(24));
        }

        private string GenerateThreadTitle(ConversationType type)
        {
            return type switch
            {
                ConversationType.DAILY_PROMPT => $"Daily Reflection - {DateTime.Now:MMM dd}",
                ConversationType.BURNING_FIRE => $"Urgent Issue - {DateTime.Now:HH:mm}",
                ConversationType.GENERAL_CHAT => $"Leadership Chat - {DateTime.Now:MMM dd}",
                ConversationType.TINKER_LEVEL => $"Tinker Issue - {DateTime.Now:HH:mm}",
                _ => $"Conversation - {DateTime.Now:MMM dd HH:mm}"
            };
        }

        private async Task<int> GetUserCurrentDay(string userId)
        {
            // Reuse existing logic from ChatController
            try
            {
                using var conn = new Npgsql.NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                
                var cmd = new Npgsql.NpgsqlCommand(@"
                    SELECT COALESCE(up.current_day, 1) as current_day
                    FROM users u
                    LEFT JOIN user_profiles up ON u.id::text = up.user_id::text
                    WHERE u.id::text = $1 OR u.email = $1", conn);
                    
                cmd.Parameters.AddWithValue(userId);
                
                var result = await cmd.ExecuteScalarAsync();
                return result != null ? Convert.ToInt32(result) : 1;
            }
            catch
            {
                return 1;
            }
        }

        private async Task PersistThreadToDatabase(ConversationThread thread)
        {
            try
            {
                using var conn = new Npgsql.NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                
                var cmd = new Npgsql.NpgsqlCommand(@"
                    INSERT INTO conversation_threads 
                    (thread_id, user_id, type, title, status, metadata, created_at, updated_at)
                    VALUES ($1::uuid, $2::uuid, $3, $4, $5, $6::jsonb, $7, $8)
                    ON CONFLICT (thread_id) DO UPDATE SET
                    status = $5,
                    metadata = $6::jsonb,
                    updated_at = $8", conn);
                
                cmd.Parameters.AddWithValue(thread.ThreadId);
                cmd.Parameters.AddWithValue(thread.UserId);
                cmd.Parameters.AddWithValue(thread.Type.ToString());
                cmd.Parameters.AddWithValue(thread.Title);
                cmd.Parameters.AddWithValue(thread.Status.ToString());
                cmd.Parameters.AddWithValue(JsonSerializer.Serialize(thread.Metadata));
                cmd.Parameters.AddWithValue(thread.StartTime);
                cmd.Parameters.AddWithValue(thread.LastActive);
                
                await cmd.ExecuteNonQueryAsync();

                // Save messages
                foreach (var msg in thread.Messages)
                {
                    var msgCmd = new Npgsql.NpgsqlCommand(@"
                        INSERT INTO thread_messages 
                        (thread_id, role, content, timestamp)
                        VALUES ($1::uuid, $2, $3, $4)
                        ON CONFLICT DO NOTHING", conn);
                    
                    msgCmd.Parameters.AddWithValue(thread.ThreadId);
                    msgCmd.Parameters.AddWithValue(msg.Role);
                    msgCmd.Parameters.AddWithValue(msg.Content);
                    msgCmd.Parameters.AddWithValue(msg.Timestamp);
                    
                    await msgCmd.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error persisting thread {ThreadId} to database", thread.ThreadId);
            }
        }

        private async Task<List<ConversationThread>> GetHistoricalThreads(string userId, int limit)
        {
            var threads = new List<ConversationThread>();
            
            try
            {
                using var conn = new Npgsql.NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                
                var cmd = new Npgsql.NpgsqlCommand(@"
                    SELECT thread_id, type, title, status, metadata, created_at, updated_at
                    FROM conversation_threads
                    WHERE user_id = $1::uuid
                    ORDER BY updated_at DESC
                    LIMIT $2", conn);
                
                cmd.Parameters.AddWithValue(userId);
                cmd.Parameters.AddWithValue(limit);
                
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    threads.Add(new ConversationThread
                    {
                        ThreadId = reader.GetGuid(0).ToString(),
                        UserId = userId,
                        Type = Enum.Parse<ConversationType>(reader.GetString(1)),
                        Title = reader.GetString(2),
                        Status = Enum.Parse<ThreadStatus>(reader.GetString(3)),
                        Metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(reader.GetString(4)) ?? new(),
                        StartTime = reader.GetDateTime(5),
                        LastActive = reader.GetDateTime(6),
                        Messages = new List<ThreadMessage>()
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting historical threads for user {UserId}", userId);
            }
            
            return threads;
        }
    }

    // Models
    public class UserSession
    {
        public string SessionId { get; set; } = "";
        public string UserId { get; set; } = "";
        public DateTime StartTime { get; set; }
        public DateTime LastActive { get; set; }
        public Dictionary<ConversationType, string> ActiveThreads { get; set; } = new();
        public ConversationType CurrentContext { get; set; }
    }

    public class ConversationThread
    {
        public string ThreadId { get; set; } = "";
        public string UserId { get; set; } = "";
        public ConversationType Type { get; set; }
        public string Title { get; set; } = "";
        public DateTime StartTime { get; set; }
        public DateTime LastActive { get; set; }
        public List<ThreadMessage> Messages { get; set; } = new();
        public ThreadStatus Status { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    public class ThreadMessage
    {
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    public enum ConversationType
    {
        DAILY_PROMPT,
        BURNING_FIRE,
        GENERAL_CHAT,
        TINKER_LEVEL,
        SESSION_END
    }

    public enum ThreadStatus
    {
        Active,
        Paused,
        Completed,
        Archived
    }
}
