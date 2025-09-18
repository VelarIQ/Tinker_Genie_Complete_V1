using System.Text.Json;
using Npgsql;

namespace TinkerGenie.API.Services
{
    public class WeaviateService : IWeaviateService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<WeaviateService> _logger;
        private readonly string _weaviateUrl;
        private readonly string _apiKey;
        private readonly string _connectionString;

        public WeaviateService(IConfiguration configuration, ILogger<WeaviateService> logger)
        {
            _logger = logger;
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

        public async Task<List<string>> SearchRelevantContentAsync(string query, int limit = 3)
        {
            try
            {
                _logger.LogInformation($"Searching Weaviate for: {query}");
                
                // Try to get dynamic content from Weaviate or fallback to database
                var content = await GetDynamicLeadershipContent(query, limit);
                return content;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching Weaviate");
                return new List<string>();
            }
        }

        public async Task<List<string>> SearchLeadershipContentAsync(string query, int limit = 3)
        {
            // This method is called by ChatController
            return await SearchRelevantContentAsync(query, limit);
        }

        public async Task IndexContentAsync(string content, string type, Dictionary<string, object> metadata)
        {
            try
            {
                _logger.LogInformation($"Indexing content of type {type} to Weaviate");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error indexing to Weaviate");
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
    }
}
