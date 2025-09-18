using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using TinkerGenie.API.Hubs;

namespace TinkerGenie.API.Services
{
    public class WebSocketGatewayService : BackgroundService
    {
        private readonly ILogger<WebSocketGatewayService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly ConcurrentDictionary<string, WebSocketConnection> _connections;
        private readonly IHubContext<ChatHub> _hubContext;

        public WebSocketGatewayService(
            ILogger<WebSocketGatewayService> logger, 
            IServiceProvider serviceProvider,
            IHubContext<ChatHub> hubContext)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _connections = new ConcurrentDictionary<string, WebSocketConnection>();
            _hubContext = hubContext;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("WebSocket Gateway Service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessWebSocketMessages(stoppingToken);
                    await Task.Delay(100, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in WebSocket Gateway Service");
                    await Task.Delay(1000, stoppingToken);
                }
            }
        }

        private Task ProcessWebSocketMessages(CancellationToken cancellationToken)
        {
            // Process any queued WebSocket messages
            // This would integrate with Redis streams for message queuing
            return Task.CompletedTask;
        }

        public Task HandleConnectionAsync(string connectionId, string userId)
        {
            var connection = new WebSocketConnection
            {
                ConnectionId = connectionId,
                UserId = userId,
                ConnectedAt = DateTime.UtcNow,
                LastActivity = DateTime.UtcNow
            };

            _connections.TryAdd(connectionId, connection);
            _logger.LogInformation("WebSocket connection {ConnectionId} established for user {UserId}", connectionId, userId);
            return Task.CompletedTask;
        }

        public Task HandleDisconnectionAsync(string connectionId)
        {
            if (_connections.TryRemove(connectionId, out var connection))
            {
                _logger.LogInformation("WebSocket connection {ConnectionId} disconnected for user {UserId}", 
                    connectionId, connection.UserId);
            }
            return Task.CompletedTask;
        }

        public async Task BroadcastMessageAsync(string message, string? excludeConnectionId = null)
        {
            if (excludeConnectionId != null)
            {
                await _hubContext.Clients.AllExcept(excludeConnectionId).SendAsync("ReceiveMessage", message);
            }
            else
            {
                await _hubContext.Clients.All.SendAsync("ReceiveMessage", message);
            }
        }

        public async Task SendToUserAsync(string userId, string message)
        {
            var userConnections = _connections.Values.Where(c => c.UserId == userId).ToList();
            foreach (var connection in userConnections)
            {
                await _hubContext.Clients.Client(connection.ConnectionId).SendAsync("ReceiveMessage", message);
            }
        }

        public int GetActiveConnectionCount()
        {
            return _connections.Count;
        }

        public int GetUserConnectionCount(string userId)
        {
            return _connections.Values.Count(c => c.UserId == userId);
        }
    }

    public class WebSocketConnection
    {
        public string ConnectionId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public DateTime ConnectedAt { get; set; }
        public DateTime LastActivity { get; set; }
        public bool IsActive => DateTime.UtcNow - LastActivity < TimeSpan.FromMinutes(5);
    }
}
