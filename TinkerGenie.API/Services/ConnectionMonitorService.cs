using Microsoft.AspNetCore.SignalR;
using TinkerGenie.API.Hubs;
using System.Collections.Concurrent;
using StackExchange.Redis;

namespace TinkerGenie.API.Services
{
    public interface IConnectionMonitorService
    {
        void OnConnected(string connectionId, string userId);
        void OnDisconnected(string connectionId);
        int GetTotalConnections();
        int GetUserConnections(string userId);
        Dictionary<string, int> GetConnectionStats();
    }

    public class ConnectionMonitorService : BackgroundService, IConnectionMonitorService
    {
        private readonly ILogger<ConnectionMonitorService> _logger;
        private readonly IHubContext<ChatHub> _chatHub;
        private readonly IConnectionMultiplexer _redis;
        private readonly ConcurrentDictionary<string, string> _connections;
        private readonly ConcurrentDictionary<string, HashSet<string>> _userConnections;

        public ConnectionMonitorService(
            ILogger<ConnectionMonitorService> logger,
            IHubContext<ChatHub> chatHub,
            IConnectionMultiplexer redis)
        {
            _logger = logger;
            _chatHub = chatHub;
            _redis = redis;
            _connections = new ConcurrentDictionary<string, string>();
            _userConnections = new ConcurrentDictionary<string, HashSet<string>>();
        }

        public void OnConnected(string connectionId, string userId)
        {
            _connections.TryAdd(connectionId, userId);
            
            _userConnections.AddOrUpdate(userId,
                new HashSet<string> { connectionId },
                (key, set) =>
                {
                    set.Add(connectionId);
                    return set;
                });

            var totalConnections = _connections.Count;
            _logger.LogInformation("WebSocket connected: {ConnectionId} for user {UserId}. Total: {Total}", 
                connectionId, userId, totalConnections);

            // Store in Redis for cross-instance tracking
            Task.Run(async () =>
            {
                var db = _redis.GetDatabase();
                await db.HashSetAsync($"connections:{Environment.MachineName}", connectionId, userId);
                await db.StringSetAsync($"connection:{connectionId}", userId, TimeSpan.FromMinutes(30));
            });

            // Alert if approaching capacity
            if (totalConnections > 60) // 80% of 75 per server
            {
                _logger.LogWarning("High connection count on instance: {Count}/75", totalConnections);
            }
        }

        public void OnDisconnected(string connectionId)
        {
            if (_connections.TryRemove(connectionId, out var userId))
            {
                if (_userConnections.TryGetValue(userId, out var connections))
                {
                    connections.Remove(connectionId);
                    if (connections.Count == 0)
                    {
                        _userConnections.TryRemove(userId, out _);
                    }
                }

                var totalConnections = _connections.Count;
                _logger.LogInformation("WebSocket disconnected: {ConnectionId}. Total: {Total}", 
                    connectionId, totalConnections);

                // Remove from Redis
                Task.Run(async () =>
                {
                    var db = _redis.GetDatabase();
                    await db.HashDeleteAsync($"connections:{Environment.MachineName}", connectionId);
                    await db.KeyDeleteAsync($"connection:{connectionId}");
                });
            }
        }

        public int GetTotalConnections()
        {
            return _connections.Count;
        }

        public int GetUserConnections(string userId)
        {
            return _userConnections.TryGetValue(userId, out var connections) 
                ? connections.Count 
                : 0;
        }

        public Dictionary<string, int> GetConnectionStats()
        {
            return new Dictionary<string, int>
            {
                ["total_connections"] = _connections.Count,
                ["unique_users"] = _userConnections.Count,
                ["server_capacity"] = 75,
                ["capacity_percentage"] = (_connections.Count * 100) / 75
            };
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Log metrics every 30 seconds
                    var stats = GetConnectionStats();
                    _logger.LogInformation("WebSocket Stats - Connections: {Total}, Users: {Users}, Capacity: {Capacity}%",
                        stats["total_connections"],
                        stats["unique_users"],
                        stats["capacity_percentage"]);

                    // Check cross-instance total from Redis
                    var db = _redis.GetDatabase();
                    var allServers = await db.ExecuteAsync("KEYS", "connections:*");
                    
                    // Send metrics to monitoring (optional)
                    if (stats["capacity_percentage"] > 80)
                    {
                        _logger.LogWarning("Instance approaching capacity: {Percentage}% - Consider scaling", 
                            stats["capacity_percentage"]);
                        
                        // Optionally notify admins via SignalR
                        await _chatHub.Clients.Group("admins").SendAsync("ServerAlert", new
                        {
                            type = "capacity_warning",
                            server = Environment.MachineName,
                            connections = stats["total_connections"],
                            capacity = stats["capacity_percentage"]
                        });
                    }

                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in connection monitoring");
                    await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
                }
            }
        }
    }
}
