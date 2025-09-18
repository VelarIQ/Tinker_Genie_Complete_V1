using StackExchange.Redis;
using System.Text.Json;

namespace TinkerGenie.API.Services
{
    public interface IRedisService
    {
        Task<string?> GetAsync(string key);
        Task<bool> SetAsync(string key, string value, TimeSpan? expiry = null);
        Task<bool> DeleteAsync(string key);
        Task<bool> ExistsAsync(string key);
        Task<List<string>> GetKeysAsync(string pattern);
    }

    public class RedisService : IRedisService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly IDatabase _db;
        private readonly ILogger<RedisService> _logger;

        public RedisService(IConnectionMultiplexer redis, ILogger<RedisService> logger)
        {
            _redis = redis;
            _db = redis.GetDatabase();
            _logger = logger;
        }

        public async Task<string?> GetAsync(string key)
        {
            try
            {
                var value = await _db.StringGetAsync(key);
                return value.IsNullOrEmpty ? null : value.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting value from Redis for key: {Key}", key);
                return null;
            }
        }

        public async Task<bool> SetAsync(string key, string value, TimeSpan? expiry = null)
        {
            try
            {
                return await _db.StringSetAsync(key, value, expiry);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting value in Redis for key: {Key}", key);
                return false;
            }
        }

        public async Task<bool> DeleteAsync(string key)
        {
            try
            {
                return await _db.KeyDeleteAsync(key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting key from Redis: {Key}", key);
                return false;
            }
        }

        public async Task<bool> ExistsAsync(string key)
        {
            try
            {
                return await _db.KeyExistsAsync(key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking key existence in Redis: {Key}", key);
                return false;
            }
        }

        public async Task<List<string>> GetKeysAsync(string pattern)
        {
            try
            {
                var server = _redis.GetServer(_redis.GetEndPoints()[0]);
                var keys = new List<string>();
                
                await foreach (var key in server.KeysAsync(pattern: pattern))
                {
                    keys.Add(key.ToString());
                }
                
                return keys;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting keys from Redis with pattern: {Pattern}", pattern);
                return new List<string>();
            }
        }
    }
}
