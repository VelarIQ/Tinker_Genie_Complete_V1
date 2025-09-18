using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;

namespace TinkerGenie.API.Services
{
    /// <summary>
    /// Service for managing user data isolation and privacy
    /// Ensures each user has their own data space for compliance and security
    /// </summary>
    public interface IUserDataIsolationService
    {
        string GetUserId(ClaimsPrincipal? user);
        string GetUserCollectionName(string userId);
        string GetUserNamespace(string userId);
        bool IsValidUserId(string userId);
        Task<bool> EnsureUserCollectionExistsAsync(string userId);
        Task<bool> DeleteUserDataAsync(string userId);
        Task<List<string>> GetUserCollectionsAsync();
    }

    public class UserDataIsolationService : IUserDataIsolationService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<UserDataIsolationService> _logger;
        private readonly string _weaviateUrl;
        private readonly string _apiKey;

        public UserDataIsolationService(IConfiguration configuration, ILogger<UserDataIsolationService> logger)
        {
            _logger = logger;
            _httpClient = new HttpClient();
            
            _weaviateUrl = configuration["Weaviate:Url"] ?? "http://localhost:8082";
            _apiKey = configuration["Weaviate:ApiKey"] ?? "";
            
            if (!string.IsNullOrEmpty(_apiKey))
            {
                _httpClient.DefaultRequestHeaders.Add("X-Weaviate-Api-Key", _apiKey);
            }
        }

        public string GetUserId(ClaimsPrincipal? user)
        {
            if (user == null)
            {
                _logger.LogWarning("No user context available");
                return "anonymous";
            }

            // Try to get userId from JWT claims
            var userId = user.FindFirst("userId")?.Value 
                        ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                        ?? user.FindFirst("sub")?.Value
                        ?? user.Identity?.Name;

            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("No userId found in user claims");
                return "anonymous";
            }

            // Sanitize userId for use in collection names
            return SanitizeUserId(userId);
        }

        public string GetUserCollectionName(string userId)
        {
            if (!IsValidUserId(userId))
            {
                throw new ArgumentException("Invalid userId", nameof(userId));
            }

            // Create user-specific collection name
            // Format: User_{Username}_Data (matching existing Chris collection format)
            // For Chris, we need to use the original username without sanitization
            if (userId.ToLower() == "chris")
            {
                return "User_Chris_Data";
            }
            
            // For other users, use their original username with proper casing
            // We'll extract the username from the sanitized ID if needed
            var originalUsername = GetOriginalUsername(userId);
            return $"User_{originalUsername}_Data";
        }

        public string GetUserNamespace(string userId)
        {
            if (!IsValidUserId(userId))
            {
                throw new ArgumentException("Invalid userId", nameof(userId));
            }

            // Create user-specific namespace for additional isolation
            return $"user_{userId}";
        }

        public bool IsValidUserId(string userId)
        {
            return !string.IsNullOrEmpty(userId) 
                   && userId != "anonymous" 
                   && userId.Length > 0 
                   && userId.Length <= 50
                   && !userId.Contains("..")
                   && !userId.Contains("/")
                   && !userId.Contains("\\");
        }

        public async Task<bool> EnsureUserCollectionExistsAsync(string userId)
        {
            try
            {
                if (!IsValidUserId(userId))
                {
                    _logger.LogWarning("Invalid userId for collection creation: {UserId}", userId);
                    return false;
                }

                var collectionName = GetUserCollectionName(userId);
                
                // Check if collection already exists
                var exists = await CollectionExistsAsync(collectionName);
                if (exists)
                {
                    _logger.LogInformation("User collection already exists: {CollectionName}", collectionName);
                    return true;
                }

                // Create user-specific collection
                return await CreateUserCollectionAsync(collectionName, userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ensuring user collection exists for userId: {UserId}", userId);
                return false;
            }
        }

        public async Task<bool> DeleteUserDataAsync(string userId)
        {
            try
            {
                if (!IsValidUserId(userId))
                {
                    _logger.LogWarning("Invalid userId for data deletion: {UserId}", userId);
                    return false;
                }

                var collectionName = GetUserCollectionName(userId);
                
                // Delete the user's collection
                var response = await _httpClient.DeleteAsync($"{_weaviateUrl}/v1/schema/{collectionName}");
                
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Successfully deleted user data for userId: {UserId}", userId);
                    return true;
                }
                else
                {
                    _logger.LogWarning("Failed to delete user data for userId: {UserId}. Status: {Status}", 
                        userId, response.StatusCode);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting user data for userId: {UserId}", userId);
                return false;
            }
        }

        public async Task<List<string>> GetUserCollectionsAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_weaviateUrl}/v1/schema");
                
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to get collections. Status: {Status}", response.StatusCode);
                    return new List<string>();
                }

                var content = await response.Content.ReadAsStringAsync();
                var schema = JsonSerializer.Deserialize<JsonElement>(content);
                
                var collections = new List<string>();
                if (schema.TryGetProperty("classes", out var classes))
                {
                    foreach (var classElement in classes.EnumerateArray())
                    {
                        if (classElement.TryGetProperty("class", out var className))
                        {
                            var name = className.GetString();
                            if (!string.IsNullOrEmpty(name) && name.StartsWith("user_"))
                            {
                                collections.Add(name);
                            }
                        }
                    }
                }

                return collections;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user collections");
                return new List<string>();
            }
        }

        private string SanitizeUserId(string userId)
        {
            // Sanitize userId for safe use in collection names
            return userId
                .Replace("@", "_at_")
                .Replace(".", "_dot_")
                .Replace("+", "_plus_")
                .Replace("-", "_dash_")
                .Replace(" ", "_")
                .ToLowerInvariant();
        }
        
        private string GetOriginalUsername(string userId)
        {
            // For usernames that have been sanitized, try to get the original
            // For now, we'll use the userId as-is since we're dealing with simple usernames
            // This preserves the casing for collection names
            if (userId.Contains("_at_") || userId.Contains("_dot_"))
            {
                // This was an email, extract the username part
                var parts = userId.Split("_at_");
                if (parts.Length > 0)
                {
                    return char.ToUpper(parts[0][0]) + parts[0].Substring(1);
                }
            }
            
            // Capitalize first letter for consistency with existing pattern
            if (!string.IsNullOrEmpty(userId))
            {
                return char.ToUpper(userId[0]) + userId.Substring(1).ToLower();
            }
            
            return userId;
        }

        private async Task<bool> CollectionExistsAsync(string collectionName)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_weaviateUrl}/v1/schema/{collectionName}");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> CreateUserCollectionAsync(string collectionName, string userId)
        {
            try
            {
                var collectionSchema = new
                {
                    @class = collectionName,
                    description = $"User-specific data collection for user: {userId}",
                    vectorizer = "text2vec-transformers",
                    properties = new[]
                    {
                        new
                        {
                            name = "content",
                            dataType = new[] { "text" },
                            description = "The content text"
                        },
                        new
                        {
                            name = "userId",
                            dataType = new[] { "string" },
                            description = "The user ID who owns this data"
                        },
                        new
                        {
                            name = "type",
                            dataType = new[] { "string" },
                            description = "The type of content"
                        },
                        new
                        {
                            name = "metadata",
                            dataType = new[] { "object" },
                            description = "Additional metadata"
                        },
                        new
                        {
                            name = "createdAt",
                            dataType = new[] { "date" },
                            description = "When this content was created"
                        }
                    }
                };

                var json = JsonSerializer.Serialize(collectionSchema);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync($"{_weaviateUrl}/v1/schema", content);
                
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Successfully created user collection: {CollectionName}", collectionName);
                    return true;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to create user collection: {CollectionName}. Error: {Error}", 
                        collectionName, errorContent);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating user collection: {CollectionName}", collectionName);
                return false;
            }
        }
    }
}
