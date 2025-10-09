using System.Text.Json;
using StackExchange.Redis;
using Microsoft.AspNetCore.SignalR;
using TinkerGenie.API.Hubs;
using TinkerGenie.API.Utilities;

namespace TinkerGenie.API.Services
{
    public interface IRedisMessageBroker
    {
        Task PublishToUser(string userId, object message);
        Task PublishToSession(string sessionId, object message);
        Task PublishGlobalUpdate(object message);
        Task SubscribeToChannels();
    }

    public class RedisMessageBroker : IRedisMessageBroker, IHostedService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly IHubContext<ChatHub> _chatHub;
        private readonly IHubContext<SyncHub> _syncHub;
        private readonly ILogger<RedisMessageBroker> _logger;
        private ISubscriber? _subscriber;

        private const string USER_CHANNEL_PREFIX = "user:";
        private const string SESSION_CHANNEL_PREFIX = "session:";
        private const string GLOBAL_CHANNEL = "global:updates";
        private const string FAILOVER_CHANNEL = "api:failover";

        public RedisMessageBroker(
            IConnectionMultiplexer redis,
            IHubContext<ChatHub> chatHub,
            IHubContext<SyncHub> syncHub,
            ILogger<RedisMessageBroker> logger)
        {
            _redis = redis;
            _chatHub = chatHub;
            _syncHub = syncHub;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting Redis Message Broker");
            await SubscribeToChannels();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping Redis Message Broker");
            _subscriber?.UnsubscribeAll();
            return Task.CompletedTask;
        }

        public async Task SubscribeToChannels()
        {
            _subscriber = _redis.GetSubscriber();

            // Subscribe to global updates
            await _subscriber.SubscribeAsync(RedisChannel.Literal(GLOBAL_CHANNEL), async (channel, message) =>
            {
                try
                {
                    var update = JsonSerializationHelper.Deserialize<BroadcastMessage>(message!);
                    if (update != null)
                    {
                        await _chatHub.Clients.All.SendAsync("GlobalUpdate", update);
                        _logger.LogDebug("Broadcasted global update: {Type}", update.Type);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing global update");
                }
            });

            // Subscribe to failover notifications
            await _subscriber.SubscribeAsync(RedisChannel.Literal(FAILOVER_CHANNEL), async (channel, message) =>
            {
                try
                {
                    var failoverInfo = JsonSerializationHelper.Deserialize<FailoverMessage>(message!);
                    if (failoverInfo != null)
                    {
                        _logger.LogWarning("API Failover detected: {FromServer} -> {ToServer}", 
                            failoverInfo.FromServer, failoverInfo.ToServer);
                        
                        // Notify connected clients to reconnect
                        await _chatHub.Clients.All.SendAsync("ServerFailover", new
                        {
                            reason = "Server maintenance",
                            reconnectUrl = failoverInfo.ToServer,
                            reconnectDelay = 2000
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing failover notification");
                }
            });

            // Subscribe to pattern for user-specific messages
            await _subscriber.SubscribeAsync(RedisChannel.Pattern($"{USER_CHANNEL_PREFIX}*"), async (channel, message) =>
            {
                try
                {
                    var userId = channel.ToString().Replace(USER_CHANNEL_PREFIX, "");
                    var userMessage = JsonSerializationHelper.Deserialize<UserMessage>(message!);
                    
                    if (userMessage != null)
                    {
                        // Send to specific user via SignalR
                        await _chatHub.Clients.User(userId).SendAsync("UserMessage", userMessage);
                        _logger.LogDebug("Sent message to user {UserId}", userId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing user message");
                }
            });

            // Subscribe to session updates
            await _subscriber.SubscribeAsync(RedisChannel.Pattern($"{SESSION_CHANNEL_PREFIX}*"), async (channel, message) =>
            {
                try
                {
                    var sessionId = channel.ToString().Replace(SESSION_CHANNEL_PREFIX, "");
                    var sessionUpdate = JsonSerializationHelper.Deserialize<SessionUpdate>(message!);
                    
                    if (sessionUpdate != null)
                    {
                        // Broadcast to all clients in this session
                        await _chatHub.Clients.Group(sessionId).SendAsync("SessionUpdate", sessionUpdate);
                        await _syncHub.Clients.Group(sessionId).SendAsync("SyncUpdate", sessionUpdate);
                        
                        _logger.LogDebug("Broadcasted session update to {SessionId}", sessionId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing session update");
                }
            });

            _logger.LogInformation("Redis Message Broker subscribed to all channels");
        }

        public async Task PublishToUser(string userId, object message)
        {
            var subscriber = _redis.GetSubscriber();
            var channel = $"{USER_CHANNEL_PREFIX}{userId}";
            var json = JsonSerializationHelper.Serialize(message);
            
            await subscriber.PublishAsync(RedisChannel.Literal(channel), json);
            _logger.LogDebug("Published message to user channel: {Channel}", channel);
        }

        public async Task PublishToSession(string sessionId, object message)
        {
            var subscriber = _redis.GetSubscriber();
            var channel = $"{SESSION_CHANNEL_PREFIX}{sessionId}";
            var json = JsonSerializationHelper.Serialize(message);
            
            await subscriber.PublishAsync(RedisChannel.Literal(channel), json);
            _logger.LogDebug("Published message to session channel: {Channel}", channel);
        }

        public async Task PublishGlobalUpdate(object message)
        {
            var subscriber = _redis.GetSubscriber();
            var json = JsonSerializationHelper.Serialize(message);
            
            await subscriber.PublishAsync(RedisChannel.Literal(GLOBAL_CHANNEL), json);
            _logger.LogDebug("Published global update");
        }

        // Health check for Redis connectivity
        public async Task<bool> IsHealthy()
        {
            try
            {
                var db = _redis.GetDatabase();
                await db.PingAsync();
                return _redis.IsConnected;
            }
            catch
            {
                return false;
            }
        }
    }

    // Message types
    public class BroadcastMessage
    {
        public string Type { get; set; } = "";
        public object Data { get; set; } = new { };
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class UserMessage
    {
        public string UserId { get; set; } = "";
        public string Content { get; set; } = "";
        public string Type { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class SessionUpdate
    {
        public string SessionId { get; set; } = "";
        public string UpdateType { get; set; } = "";
        public object Data { get; set; } = new { };
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class FailoverMessage
    {
        public string FromServer { get; set; } = "";
        public string ToServer { get; set; } = "";
        public string Reason { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
