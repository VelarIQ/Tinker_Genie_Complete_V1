using TinkerGenie.API.Models;
using Dapper;
using Npgsql;

namespace TinkerGenie.API.Services
{
    public class LeadershipService : ILeadershipService
    {
        private readonly string? _connectionString;
        private readonly ILogger<LeadershipService> _logger;

        public LeadershipService(IConfiguration configuration, ILogger<LeadershipService> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? 
                Environment.GetEnvironmentVariable("DATABASE_URL") ??
                "Host=127.0.0.1;Port=6432;Database=tinker_genie;Username=genie_admin;Password=TinkerGenie1234;Pooling=true;MinPoolSize=5;MaxPoolSize=25;";
            _logger = logger;
        }

        public async Task<DailyPrompt?> GetDailyPromptAsync(string userId)
        {
            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT id, user_id, prompt_text, prompt_type, created_at, is_completed, feedback_criteria
                    FROM daily_prompts 
                    WHERE user_id = @userId 
                    AND DATE(created_at) = CURRENT_DATE
                    ORDER BY created_at DESC 
                    LIMIT 1";

                var prompt = await connection.QueryFirstOrDefaultAsync<DailyPrompt>(query, new { userId });
                return prompt;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving daily prompt for user {UserId}", userId);
                return null;
            }
        }

        public async Task<DailyPrompt> CreateDailyPromptAsync(string userId, string promptText, string promptType = "leadership")
        {
            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();

                var prompt = new DailyPrompt
                {
                    DayNumber = 1, // Default day number
                    Title = "Leadership Development",
                    PromptText = promptText,
                    FillInBlanks = new List<string>(),
                    TaskInstructions = "Complete this exercise and reflect on your leadership journey.",
                    EstimatedTimeMinutes = 15
                };

                var insertQuery = @"
                    INSERT INTO daily_prompts (id, user_id, prompt_text, prompt_type, created_at, is_completed, feedback_criteria)
                    VALUES (@Id, @UserId, @PromptText, @PromptType, @CreatedAt, @IsCompleted, @FeedbackCriteria)";

                await connection.ExecuteAsync(insertQuery, new
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    PromptText = promptText,
                    PromptType = promptType,
                    CreatedAt = DateTime.UtcNow,
                    IsCompleted = false,
                    FeedbackCriteria = "Reflect on how this applies to your current leadership challenges and share specific examples of how you might implement these insights."
                });
                
                _logger.LogInformation("Created daily prompt for user {UserId}", userId);
                return prompt;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating daily prompt for user {UserId}", userId);
                throw;
            }
        }

        public async Task<bool> MarkPromptCompletedAsync(Guid promptId, string? feedback = null)
        {
            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    UPDATE daily_prompts 
                    SET is_completed = true, 
                        completed_at = @completedAt,
                        user_feedback = @feedback
                    WHERE id = @promptId";

                var result = await connection.ExecuteAsync(query, new 
                { 
                    promptId, 
                    completedAt = DateTime.UtcNow,
                    feedback 
                });

                return result > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking prompt {PromptId} as completed", promptId);
                return false;
            }
        }

        public async Task<List<DailyPrompt>> GetUserPromptHistoryAsync(string userId, int limit = 10)
        {
            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT id, user_id, prompt_text, prompt_type, created_at, is_completed, feedback_criteria, completed_at, user_feedback
                    FROM daily_prompts 
                    WHERE user_id = @userId 
                    ORDER BY created_at DESC 
                    LIMIT @limit";

                var prompts = await connection.QueryAsync<DailyPrompt>(query, new { userId, limit });
                return prompts.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving prompt history for user {UserId}", userId);
                return new List<DailyPrompt>();
            }
        }

        public async Task<string> GenerateLeadershipInsightAsync(string context, string userId)
        {
            try
            {
                // This would integrate with OpenAI API for generating leadership insights
                // For now, return a placeholder response
                await Task.Delay(100); // Simulate async work
                
                var insights = new[]
                {
                    "Great leaders focus on developing their team's potential rather than just managing tasks.",
                    "Effective leadership requires both emotional intelligence and strategic thinking.",
                    "The best leaders create an environment where people can do their best work.",
                    "Leadership is about influence, not authority. Influence comes from trust and respect.",
                    "Successful leaders are continuous learners who adapt their style to their team's needs."
                };

                var random = new Random();
                return insights[random.Next(insights.Length)];
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating leadership insight for user {UserId}", userId);
                return "Leadership is a journey of continuous growth and learning. Focus on developing your team and creating positive impact.";
            }
        }

        public async Task<bool> CompletePromptAsync(string userId, int dayNumber, Dictionary<string, string> responses, string? reflectionNotes = null)
        {
            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    UPDATE daily_prompts 
                    SET is_completed = true, 
                        completed_at = @completedAt,
                        user_feedback = @reflectionNotes
                    WHERE user_id = @userId AND day_number = @dayNumber";

                var result = await connection.ExecuteAsync(query, new 
                { 
                    userId, 
                    dayNumber,
                    completedAt = DateTime.UtcNow,
                    reflectionNotes 
                });

                return result > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing prompt for user {UserId}, day {DayNumber}", userId, dayNumber);
                return false;
            }
        }

        public async Task<List<string>> GenerateFollowUpQuestionsAsync(int dayNumber, Dictionary<string, string> responses)
        {
            try
            {
                await Task.Delay(50); // Simulate async work
                
                var questions = new[]
                {
                    "How did this exercise change your perspective on leadership?",
                    "What specific actions will you take based on today's insights?",
                    "How can you apply these learnings in your current role?",
                    "What challenges do you anticipate when implementing these ideas?",
                    "Who in your network could benefit from these insights?"
                };

                return questions.Take(3).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating follow-up questions for day {DayNumber}", dayNumber);
                return new List<string>();
            }
        }

        public async Task<UserProgress> GetUserProgressAsync(string userId)
        {
            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT 
                        COALESCE(MAX(day_number), 0) as current_day,
                        COUNT(CASE WHEN is_completed = true THEN 1 END) as completed_days,
                        COALESCE(MAX(completed_at), '1900-01-01'::timestamp) as last_completed
                    FROM daily_prompts 
                    WHERE user_id = @userId";

                var result = await connection.QueryFirstOrDefaultAsync(query, new { userId });
                
                return new UserProgress
                {
                    CurrentDay = result?.current_day ?? 0,
                    CompletedDays = result?.completed_days ?? 0,
                    CurrentStreak = 0, // Would need more complex logic
                    LastCompleted = result?.last_completed ?? DateTime.MinValue,
                    MissedDays = new List<int>()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user progress for {UserId}", userId);
                return new UserProgress();
            }
        }

        public async Task<bool> UpdateUserPreferencesAsync(string userId, UserPreferences preferences)
        {
            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    INSERT INTO user_preferences (user_id, communication_style, prompt_delivery_time, timezone, journey_paused)
                    VALUES (@userId, @CommunicationStyle, @PromptDeliveryTime, @Timezone, @JourneyPaused)
                    ON CONFLICT (user_id) 
                    DO UPDATE SET 
                        communication_style = @CommunicationStyle,
                        prompt_delivery_time = @PromptDeliveryTime,
                        timezone = @Timezone,
                        journey_paused = @JourneyPaused";

                var result = await connection.ExecuteAsync(query, new 
                { 
                    userId,
                    preferences.CommunicationStyle,
                    preferences.PromptDeliveryTime,
                    preferences.Timezone,
                    preferences.JourneyPaused
                });

                return result > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating user preferences for {UserId}", userId);
                return false;
            }
        }

        public async Task<List<DailyPrompt>> GetMissedPromptsAsync(string userId)
        {
            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT id, user_id, prompt_text, prompt_type, created_at, is_completed, feedback_criteria
                    FROM daily_prompts 
                    WHERE user_id = @userId 
                    AND is_completed = false
                    AND created_at < CURRENT_DATE
                    ORDER BY created_at DESC";

                var prompts = await connection.QueryAsync<DailyPrompt>(query, new { userId });
                return prompts.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting missed prompts for user {UserId}", userId);
                return new List<DailyPrompt>();
            }
        }

        public async Task<bool> SendNudgeAsync(string userId, string nudgeType)
        {
            try
            {
                // This would integrate with notification service
                await Task.Delay(50); // Simulate async work
                
                _logger.LogInformation("Sending {NudgeType} nudge to user {UserId}", nudgeType, userId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending nudge to user {UserId}", userId);
                return false;
            }
        }
    }
}
