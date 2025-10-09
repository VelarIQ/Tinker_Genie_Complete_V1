using System.Text.Json;
using StackExchange.Redis;
using Microsoft.Extensions.Logging;
using TinkerGenie.API.Utilities;

namespace TinkerGenie.API.Services
{
    /// <summary>
    /// September 2025 Tenant-Aware Caching Service
    /// Provides isolated caching per tenant with automatic key prefixing
    /// </summary>
    public interface ITenantCacheService
    {
        Task<T?> GetAsync<T>(string key) where T : class;
        Task SetAsync<T>(string key, T value, TimeSpan? expiry = null) where T : class;
        Task RemoveAsync(string key);
        Task InvalidateTenantCacheAsync();
        Task InvalidatePatternAsync(string pattern);
        Task<bool> ExistsAsync(string key);
        Task<long> IncrementAsync(string key, long value = 1, TimeSpan? expiry = null);
        Task<Dictionary<string, T?>> GetMultipleAsync<T>(IEnumerable<string> keys) where T : class;
    }

    public class TenantCacheService : ITenantCacheService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly ITenantResolver _tenantResolver;
        private readonly ILogger<TenantCacheService> _logger;
        private readonly JsonSerializerOptions _jsonOptions;

        public TenantCacheService(
            IConnectionMultiplexer redis,
            ITenantResolver tenantResolver,
            ILogger<TenantCacheService> logger)
        {
            _redis = redis;
            _tenantResolver = tenantResolver;
            _logger = logger;
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };
        }

        public async Task<T?> GetAsync<T>(string key) where T : class
        {
            try
            {
                var database = _redis.GetDatabase();
                var tenantKey = GetTenantKey(key);
                
                var value = await database.StringGetAsync(tenantKey);
                if (!value.HasValue)
                    return null;

                var result = JsonSerializationHelper.Deserialize<T>(value!, _jsonOptions);
                _logger.LogDebug("Cache hit for key: {Key}", tenantKey);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting cached value for key: {Key}", key);
                return null;
            }
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null) where T : class
        {
            try
            {
                var database = _redis.GetDatabase();
                var tenantKey = GetTenantKey(key);
                var serializedValue = JsonSerializationHelper.Serialize(value, _jsonOptions);

                await database.StringSetAsync(tenantKey, serializedValue, expiry);
                _logger.LogDebug("Cache set for key: {Key}, expiry: {Expiry}", tenantKey, expiry);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting cached value for key: {Key}", key);
            }
        }

        public async Task RemoveAsync(string key)
        {
            try
            {
                var database = _redis.GetDatabase();
                var tenantKey = GetTenantKey(key);
                
                await database.KeyDeleteAsync(tenantKey);
                _logger.LogDebug("Cache removed for key: {Key}", tenantKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing cached value for key: {Key}", key);
            }
        }

        public async Task InvalidateTenantCacheAsync()
        {
            try
            {
                var tenantId = _tenantResolver.GetCurrentTenantId();
                var pattern = $"tenant:{tenantId}:*";
                
                await InvalidatePatternAsync(pattern);
                _logger.LogInformation("Invalidated all cache for tenant: {TenantId}", tenantId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error invalidating tenant cache");
            }
        }

        public async Task InvalidatePatternAsync(string pattern)
        {
            try
            {
                var server = _redis.GetServer(_redis.GetEndPoints().First());
                var keys = server.Keys(pattern: pattern);
                
                var database = _redis.GetDatabase();
                var keyArray = keys.ToArray();
                
                if (keyArray.Length > 0)
                {
                    await database.KeyDeleteAsync(keyArray);
                    _logger.LogDebug("Invalidated {Count} keys matching pattern: {Pattern}", keyArray.Length, pattern);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error invalidating cache pattern: {Pattern}", pattern);
            }
        }

        public async Task<bool> ExistsAsync(string key)
        {
            try
            {
                var database = _redis.GetDatabase();
                var tenantKey = GetTenantKey(key);
                
                return await database.KeyExistsAsync(tenantKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking cache existence for key: {Key}", key);
                return false;
            }
        }

        public async Task<long> IncrementAsync(string key, long value = 1, TimeSpan? expiry = null)
        {
            try
            {
                var database = _redis.GetDatabase();
                var tenantKey = GetTenantKey(key);
                
                var result = await database.StringIncrementAsync(tenantKey, value);
                
                if (expiry.HasValue)
                {
                    await database.KeyExpireAsync(tenantKey, expiry.Value);
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error incrementing cache value for key: {Key}", key);
                return 0;
            }
        }

        public async Task<Dictionary<string, T?>> GetMultipleAsync<T>(IEnumerable<string> keys) where T : class
        {
            var result = new Dictionary<string, T?>();
            
            try
            {
                var database = _redis.GetDatabase();
                var tenantKeys = keys.Select(k => (RedisKey)GetTenantKey(k)).ToArray();
                var originalKeys = keys.ToArray();
                
                var values = await database.StringGetAsync(tenantKeys);
                
                for (int i = 0; i < originalKeys.Length; i++)
                {
                    var originalKey = originalKeys[i];
                    var value = values[i];
                    
                    if (value.HasValue)
                    {
                        try
                        {
                            var deserializedValue = JsonSerializationHelper.Deserialize<T>(value!, _jsonOptions);
                            result[originalKey] = deserializedValue;
                        }
                        catch (JsonException ex)
                        {
                            _logger.LogWarning(ex, "Error deserializing cached value for key: {Key}", originalKey);
                            result[originalKey] = null;
                        }
                    }
                    else
                    {
                        result[originalKey] = null;
                    }
                }
                
                _logger.LogDebug("Retrieved {Count} keys from cache", originalKeys.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting multiple cached values");
            }
            
            return result;
        }

        private string GetTenantKey(string key)
        {
            var tenantId = _tenantResolver.GetCurrentTenantId();
            return $"tenant:{tenantId}:{key}";
        }
    }

    /// <summary>
    /// Cache key constants for consistent naming
    /// </summary>
    public static class CacheKeys
    {
        public const string TENANT_CONFIG = "config";
        public const string USER_PROFILE = "user_profile";
        public const string CONVERSATION_HISTORY = "conversation_history";
        public const string DAILY_PROMPTS = "daily_prompts";
        public const string USER_PREFERENCES = "user_preferences";
        public const string API_RATE_LIMIT = "api_rate_limit";
        public const string FEATURE_FLAGS = "feature_flags";
        
        public static string UserProfile(Guid userId) => $"{USER_PROFILE}:{userId}";
        public static string ConversationHistory(Guid userId) => $"{CONVERSATION_HISTORY}:{userId}";
        public static string UserPreferences(Guid userId) => $"{USER_PREFERENCES}:{userId}";
        public static string ApiRateLimit(Guid userId) => $"{API_RATE_LIMIT}:{userId}";
    }
}
