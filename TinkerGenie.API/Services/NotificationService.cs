using System.Text.Json;
using Npgsql;

namespace TinkerGenie.API.Services
{
    public interface INotificationService
    {
        Task<bool> SendPushNotification(string userId, string title, string body, Dictionary<string, string>? data = null);
        Task<bool> SendEmailNotification(string userId, string subject, string body);
        Task<bool> SavePushSubscription(string userId, string endpoint, string p256dh, string auth);
        Task<PushSubscription?> GetUserPushSubscription(string userId);
    }

    public class NotificationService : INotificationService
    {
        private readonly ILogger<NotificationService> _logger;
        private readonly string _connectionString;
        private readonly IConfiguration _configuration;

        public NotificationService(
            ILogger<NotificationService> logger,
            IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
        }

        public async Task<bool> SendPushNotification(string userId, string title, string body, Dictionary<string, string>? data = null)
        {
            try
            {
                var subscription = await GetUserPushSubscription(userId);
                if (subscription == null)
                {
                    _logger.LogWarning("No push subscription found for user {UserId}", userId);
                    return false;
                }

                // TODO: Implement actual push notification using WebPush library
                // For now, just log the attempt
                _logger.LogInformation("Would send push notification to user {UserId}: {Title}", userId, title);
                
                // Simulate async operation
                await Task.Delay(1);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending push notification to user {UserId}", userId);
                return false;
            }
        }

        public async Task<bool> SendEmailNotification(string userId, string subject, string body)
        {
            try
            {
                // TODO: Implement email sending via SMTP or service like SendGrid
                _logger.LogInformation("Would send email to user {UserId}: {Subject}", userId, subject);
                
                // Simulate async operation
                await Task.Delay(1);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending email to user {UserId}", userId);
                return false;
            }
        }

        public async Task<bool> SavePushSubscription(string userId, string endpoint, string p256dh, string auth)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                await using var cmd = new NpgsqlCommand(@"
                    INSERT INTO push_subscriptions (user_id, endpoint, p256dh, auth, is_active)
                    VALUES (@userId, @endpoint, @p256dh, @auth, true)
                    ON CONFLICT (user_id) DO UPDATE SET
                        endpoint = @endpoint,
                        p256dh = @p256dh,
                        auth = @auth,
                        is_active = true,
                        updated_at = CURRENT_TIMESTAMP", conn);

                cmd.Parameters.AddWithValue("userId", userId);
                cmd.Parameters.AddWithValue("endpoint", endpoint);
                cmd.Parameters.AddWithValue("p256dh", p256dh);
                cmd.Parameters.AddWithValue("auth", auth);

                await cmd.ExecuteNonQueryAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving push subscription for user {UserId}", userId);
                return false;
            }
        }

        public async Task<PushSubscription?> GetUserPushSubscription(string userId)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                await using var cmd = new NpgsqlCommand(@"
                    SELECT endpoint, p256dh, auth 
                    FROM push_subscriptions 
                    WHERE user_id = @userId AND is_active = true", conn);

                cmd.Parameters.AddWithValue("userId", userId);

                await using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new PushSubscription
                    {
                        Endpoint = reader.GetString(0),
                        P256dh = reader.GetString(1),
                        Auth = reader.GetString(2)
                    };
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting push subscription for user {UserId}", userId);
                return null;
            }
        }
    }

    public class PushSubscription
    {
        public string Endpoint { get; set; } = "";
        public string P256dh { get; set; } = "";
        public string Auth { get; set; } = "";
    }
}
