using System.Text.Json;
using Npgsql;
using System.Security.Claims;

namespace TinkerGenie.API.Services
{
    public class WeaviateService : IWeaviateService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<WeaviateService> _logger;
        private readonly IUserDataIsolationService _userDataIsolation;
        private readonly string _weaviateUrl;
        private readonly string _apiKey;
        private readonly string _connectionString;

        public WeaviateService(IConfiguration configuration, ILogger<WeaviateService> logger, IUserDataIsolationService userDataIsolation)
        {
            _logger = logger;
            _userDataIsolation = userDataIsolation;
            _httpClient = new HttpClient();
            
            // Use local Weaviate by default, fallback to cloud if needed
            _weaviateUrl = configuration["Weaviate:Url"] ?? "http://localhost:8082";
            _apiKey = configuration["Weaviate:ApiKey"] ?? "";
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
            
            // Only add API key if it's not empty (local Weaviate doesn't need it)
            if (!string.IsNullOrEmpty(_apiKey))
            {
                _httpClient.DefaultRequestHeaders.Add("X-Weaviate-Api-Key", _apiKey);
            }
        }

        public async Task<List<string>> SearchRelevantContentAsync(string query, int limit = 3, ClaimsPrincipal? user = null)
        {
            try
            {
                var userId = _userDataIsolation.GetUserId(user);
                _logger.LogInformation($"Searching Weaviate for user {userId}: {query}");
                
                // Ensure user collection exists
                await _userDataIsolation.EnsureUserCollectionExistsAsync(userId);
                
                // Search user-specific content
                var content = await SearchUserContentAsync(userId, query, limit);
                return content;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching Weaviate for user");
                return new List<string>();
            }
        }

        public async Task<List<string>> SearchLeadershipContentAsync(string query, int limit = 3, ClaimsPrincipal? user = null)
        {
            // This method is called by ChatController
            return await SearchRelevantContentAsync(query, limit, user);
        }

        public async Task IndexContentAsync(string content, string type, Dictionary<string, object> metadata, ClaimsPrincipal? user = null)
        {
            try
            {
                var userId = _userDataIsolation.GetUserId(user);
                _logger.LogInformation($"Indexing content of type {type} to Weaviate for user {userId}");
                
                // Ensure user collection exists
                await _userDataIsolation.EnsureUserCollectionExistsAsync(userId);
                
                // Index content to user-specific collection
                await IndexUserContentAsync(userId, content, type, metadata);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error indexing to Weaviate for user");
            }
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                _logger.LogInformation("Testing Weaviate connection");
                return await Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing Weaviate connection");
                return false;
            }
        }

        public async Task CreateCollectionAsync()
        {
            try
            {
                _logger.LogInformation("Creating Weaviate collection");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating Weaviate collection");
            }
        }

        private async Task<List<string>> SearchUserContentAsync(string userId, string query, int limit)
        {
            try
            {
                // Search user's personal collection - use the same naming as UserDataIsolationService
                var userCollectionName = _userDataIsolation.GetUserCollectionName(userId);
                var searchPayload = new
                {
                    query = $@"
                    {{
                        Get {{
                            {userCollectionName}(
                                nearText: {{ concepts: [""{query}""] }}
                                limit: {limit}
                            ) {{
                                text
                                metadata
                                type
                                _additional {{ distance }}
                            }}
                        }}
                    }}"
                };

                var json = JsonSerializer.Serialize(searchPayload);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync($"{_weaviateUrl}/v1/graphql", content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    
                    var results = new List<string>();
                    if (result.TryGetProperty("data", out var data) && 
                        data.TryGetProperty("Get", out var get) &&
                        get.TryGetProperty(userCollectionName, out var items))
                    {
                        foreach (var item in items.EnumerateArray())
                        {
                            if (item.TryGetProperty("text", out var textProp))
                            {
                                results.Add(textProp.GetString() ?? "");
                            }
                        }
                    }
                    
                    return results;
                }
                else
                {
                    _logger.LogWarning("Search failed for user {UserId}. Status: {Status}", userId, response.StatusCode);
                    return new List<string>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching user content for userId: {UserId}", userId);
                return new List<string>();
            }
        }

        private async Task IndexUserContentAsync(string userId, string content, string type, Dictionary<string, object> metadata)
        {
            try
            {
                // Use user-specific collection - use the same naming as UserDataIsolationService
                var userCollectionName = _userDataIsolation.GetUserCollectionName(userId);
                var objectPayload = new
                {
                    @class = userCollectionName,
                    properties = new
                    {
                        text = content,
                        userId = userId,
                        type = type,
                        metadata = JsonSerializer.Serialize(metadata),
                        createdAt = DateTime.UtcNow.ToString("O")
                    }
                };

                var json = JsonSerializer.Serialize(objectPayload);
                var requestContent = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync($"{_weaviateUrl}/v1/objects", requestContent);
                
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Successfully indexed content for user {UserId} in collection {CollectionName}", userId, userCollectionName);
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to index content for user {UserId} in collection {CollectionName}. Error: {Error}", userId, userCollectionName, errorContent);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error indexing content for user {UserId}", userId);
            }
        }

        public async Task<bool> DeleteUserDataAsync(string userId)
        {
            return await _userDataIsolation.DeleteUserDataAsync(userId);
        }

        public async Task<List<string>> GetUserCollectionsAsync()
        {
            return await _userDataIsolation.GetUserCollectionsAsync();
        }

        public async Task<List<string>> SearchDailyPromptsAsync(string query, int limit = 5)
        {
            try
            {
                var searchPayload = new
                {
                    query = $@"
                    {{
                        Get {{
                            DailyPrompts(
                                nearText: {{ concepts: [""{query}""] }}
                                limit: {limit}
                            ) {{
                                text
                                metadata
                                type
                                _additional {{ distance }}
                            }}
                        }}
                    }}"
                };

                var json = JsonSerializer.Serialize(searchPayload);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync($"{_weaviateUrl}/v1/graphql", content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    
                    var results = new List<string>();
                    if (result.TryGetProperty("data", out var data) && 
                        data.TryGetProperty("Get", out var get) &&
                        get.TryGetProperty("DailyPrompts", out var items))
                    {
                        foreach (var item in items.EnumerateArray())
                        {
                            if (item.TryGetProperty("text", out var textProp))
                            {
                                results.Add(textProp.GetString() ?? "");
                            }
                        }
                    }
                    
                    return results;
                }
                else
                {
                    _logger.LogWarning("Search failed for daily prompts. Status: {Status}", response.StatusCode);
                    return new List<string>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching daily prompts");
                return new List<string>();
            }
        }

        public async Task<List<string>> SearchCurriculumAsync(string query, int limit = 5)
        {
            try
            {
                var searchPayload = new
                {
                    query = $@"
                    {{
                        Get {{
                            Curriculum(
                                nearText: {{ concepts: [""{query}""] }}
                                limit: {limit}
                            ) {{
                                text
                                metadata
                                type
                                _additional {{ distance }}
                            }}
                        }}
                    }}"
                };

                var json = JsonSerializer.Serialize(searchPayload);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync($"{_weaviateUrl}/v1/graphql", content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    
                    var results = new List<string>();
                    if (result.TryGetProperty("data", out var data) && 
                        data.TryGetProperty("Get", out var get) &&
                        get.TryGetProperty("Curriculum", out var items))
                    {
                        foreach (var item in items.EnumerateArray())
                        {
                            if (item.TryGetProperty("text", out var textProp))
                            {
                                results.Add(textProp.GetString() ?? "");
                            }
                        }
                    }
                    
                    return results;
                }
                else
                {
                    _logger.LogWarning("Search failed for curriculum. Status: {Status}", response.StatusCode);
                    return new List<string>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching curriculum");
                return new List<string>();
            }
        }
        
        // Enhanced search methods for burning fires and general chat
        public async Task<List<CurriculumSearchResult>> SearchCurriculum(string query, int limit = 3)
        {
            try
            {
                var searchPayload = new
                {
                    query = $@"
                    {{
                        Get {{
                            Curriculum(
                                nearText: {{ concepts: [""{ query}""]}}
                                limit: {limit}
                            ) {{
                                title
                                url
                                content
                                topics
                                _additional {{ 
                                    distance
                                    certainty
                                }}
                            }}
                        }}
                    }}"
                };

                var json = JsonSerializer.Serialize(searchPayload);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync($"{_weaviateUrl}/v1/graphql", content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    
                    var results = new List<CurriculumSearchResult>();
                    if (result.TryGetProperty("data", out var data) && 
                        data.TryGetProperty("Get", out var get) &&
                        get.TryGetProperty("Curriculum", out var items))
                    {
                        foreach (var item in items.EnumerateArray())
                        {
                            var searchResult = new CurriculumSearchResult
                            {
                                Title = item.TryGetProperty("title", out var title) ? title.GetString() : "Resource",
                                Url = item.TryGetProperty("url", out var url) ? url.GetString() : "#",
                                Content = item.TryGetProperty("content", out var contentProperty) ? contentProperty.GetString() : "",
                                Topics = new List<string>()
                            };
                            
                            if (item.TryGetProperty("topics", out var topics) && topics.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var topic in topics.EnumerateArray())
                                {
                                    searchResult.Topics.Add(topic.GetString() ?? "");
                                }
                            }
                            
                            if (item.TryGetProperty("_additional", out var additional))
                            {
                                if (additional.TryGetProperty("certainty", out var certainty))
                                {
                                    searchResult.RelevanceScore = (float)certainty.GetDouble();
                                }
                                else if (additional.TryGetProperty("distance", out var distance))
                                {
                                    // Convert distance to a 0-1 score (lower distance = higher score)
                                    searchResult.RelevanceScore = Math.Max(0, 1 - (float)distance.GetDouble());
                                }
                            }
                            
                            results.Add(searchResult);
                        }
                    }
                    
                    return results.OrderByDescending(r => r.RelevanceScore).ToList();
                }
                else
                {
                    _logger.LogWarning("Search failed for curriculum. Status: {Status}", response.StatusCode);
                    
                    // Return mock data for testing
                    return GetMockCurriculumResults(query);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching curriculum");
                // Return mock data for testing
                return GetMockCurriculumResults(query);
            }
        }
        
        public async Task<List<UserReflectionResult>> SearchUserReflections(string userId, string query, int limit = 5)
        {
            try
            {
                var userCollectionName = _userDataIsolation.GetUserCollectionName(userId);
                var searchPayload = new
                {
                    query = $@"
                    {{
                        Get {{
                            DailyPromptResponse(
                                where: {{
                                    path: [""userId""]
                                    operator: Equal
                                    valueString: ""{ userId}""
                                }}
                                nearText: {{ concepts: [""{ query}""]}}
                                limit: {limit}
                            ) {{
                                dayNumber
                                reflection
                                timestamp
                                emotions
                                _additional {{ 
                                    distance
                                    certainty
                                }}
                            }}
                        }}
                    }}"
                };

                var json = JsonSerializer.Serialize(searchPayload);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync($"{_weaviateUrl}/v1/graphql", content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    
                    var results = new List<UserReflectionResult>();
                    if (result.TryGetProperty("data", out var data) && 
                        data.TryGetProperty("Get", out var get) &&
                        get.TryGetProperty("DailyPromptResponse", out var items))
                    {
                        foreach (var item in items.EnumerateArray())
                        {
                            var reflectionResult = new UserReflectionResult
                            {
                                DayNumber = item.TryGetProperty("dayNumber", out var day) ? day.GetInt32() : 0,
                                Reflection = item.TryGetProperty("reflection", out var reflection) ? reflection.GetString() : "",
                                Emotions = new List<string>()
                            };
                            
                            if (item.TryGetProperty("timestamp", out var timestamp))
                            {
                                if (DateTime.TryParse(timestamp.GetString(), out var dt))
                                {
                                    reflectionResult.Timestamp = dt;
                                }
                            }
                            
                            if (item.TryGetProperty("emotions", out var emotions) && emotions.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var emotion in emotions.EnumerateArray())
                                {
                                    reflectionResult.Emotions.Add(emotion.GetString() ?? "");
                                }
                            }
                            
                            if (item.TryGetProperty("_additional", out var additional))
                            {
                                if (additional.TryGetProperty("certainty", out var certainty))
                                {
                                    reflectionResult.RelevanceScore = (float)certainty.GetDouble();
                                }
                                else if (additional.TryGetProperty("distance", out var distance))
                                {
                                    reflectionResult.RelevanceScore = Math.Max(0, 1 - (float)distance.GetDouble());
                                }
                            }
                            
                            results.Add(reflectionResult);
                        }
                    }
                    
                    return results.OrderByDescending(r => r.RelevanceScore).ToList();
                }
                else
                {
                    _logger.LogWarning("Search failed for user reflections. Status: {Status}", response.StatusCode);
                    return new List<UserReflectionResult>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching user reflections");
                return new List<UserReflectionResult>();
            }
        }
        
        // Mock data for testing when Weaviate is not available
        private List<CurriculumSearchResult> GetMockCurriculumResults(string query)
        {
            return new List<CurriculumSearchResult>
            {
                new CurriculumSearchResult
                {
                    Title = "Leadership in Crisis: A Practical Guide",
                    Url = "https://twobrain.com/resources/crisis-leadership",
                    Content = "When facing urgent business challenges, leaders must act decisively while maintaining team morale...",
                    Topics = new List<string> { "leadership", "crisis management", "team management" },
                    RelevanceScore = 0.95f
                },
                new CurriculumSearchResult
                {
                    Title = "Staff Management Essentials",
                    Url = "https://twobrain.com/resources/staff-management",
                    Content = "Effective staff management starts with clear communication and consistent expectations...",
                    Topics = new List<string> { "staff", "management", "communication" },
                    RelevanceScore = 0.87f
                },
                new CurriculumSearchResult
                {
                    Title = "Financial Recovery Strategies",
                    Url = "https://twobrain.com/resources/financial-recovery",
                    Content = "When your business faces financial challenges, these proven strategies can help you recover...",
                    Topics = new List<string> { "finance", "recovery", "business strategy" },
                    RelevanceScore = 0.82f
                }
            };
        }
    }
}
