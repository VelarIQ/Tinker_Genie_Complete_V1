using System.Text.Json;
using Microsoft.Extensions.Logging;
using WeaviateNET;
using TinkerGenie.API.Services.Interfaces;

namespace TinkerGenie.API.Services
{
    public interface ICurriculumSearchService
    {
        Task<CurriculumSearchResponse> SearchForBurningFires(string issue);
        Task<CurriculumSearchResponse> SearchGeneralCurriculum(string query);
        Task<List<DailyPromptResult>> SearchPastDailyPrompts(string userId, string query);
    }

    public class CurriculumSearchService : ICurriculumSearchService
    {
        private readonly IWeaviateService _weaviateService;
        private readonly ILogger<CurriculumSearchService> _logger;
        private readonly string _connectionString;

        public CurriculumSearchService(
            IWeaviateService weaviateService,
            ILogger<CurriculumSearchService> logger,
            IConfiguration configuration)
        {
            _weaviateService = weaviateService;
            _logger = logger;
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
        }

        public async Task<CurriculumSearchResponse> SearchForBurningFires(string issue)
        {
            try
            {
                _logger.LogInformation("Searching curriculum for burning fire: {Issue}", issue);
                
                // Search Weaviate for relevant curriculum resources
                var searchQuery = new
                {
                    query = issue,
                    collection = "Curriculum",
                    limit = 3,
                    fields = new[] { "title", "url", "content", "topics", "relevance_score" }
                };

                var results = await _weaviateService.SearchCurriculumDetailedAsync(issue, 6);
                
                var searchResult = new CurriculumSearchResponse
                {
                    Query = issue,
                    Resources = results
                        .Take(3) // Assuming a limit of 3 for this specific search
                        .Select(r => new CurriculumResource
                        {
                            Title = r.Title ?? "Resource",
                            Url = string.IsNullOrWhiteSpace(r.Url) ? "#" : r.Url,
                            Preview = TruncateContent(r.Content ?? string.Empty, 200),
                            RelevanceScore = r.RelevanceScore.GetValueOrDefault(),
                            Topics = r.Topics ?? new List<string>(),
                            ResourceType = DetermineResourceType(r.Url ?? string.Empty)
                        })
                        .ToList()
                };

                // If no results from Weaviate, provide fallback resources
                if (!searchResult.Resources.Any())
                {
                    searchResult.Resources = GetFallbackResources(issue);
                }

                return searchResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching curriculum for burning fires");
                return new CurriculumSearchResponse
                {
                    Query = issue,
                    Resources = GetFallbackResources(issue)
                };
            }
        }

        public async Task<CurriculumSearchResponse> SearchGeneralCurriculum(string query)
        {
            try
            {
                _logger.LogInformation("General curriculum search: {Query}", query);
                
                var results = await _weaviateService.SearchCurriculumDetailedAsync(query, 8);
                
                var searchResult = new CurriculumSearchResponse
                {
                    Query = query,
                    Resources = results
                        .Select(r => new CurriculumResource
                        {
                            Title = r.Title ?? "Resource",
                            Url = string.IsNullOrWhiteSpace(r.Url) ? "#" : r.Url,
                            Preview = TruncateContent(r.Content ?? string.Empty, 200),
                            RelevanceScore = r.RelevanceScore.GetValueOrDefault(),
                            Topics = r.Topics ?? new List<string>(),
                            ResourceType = DetermineResourceType(r.Url ?? string.Empty)
                        })
                        .ToList()
                };

                return searchResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in general curriculum search");
                return new CurriculumSearchResponse
                {
                    Query = query,
                    Resources = new List<CurriculumResource>()
                };
            }
        }

        public async Task<List<DailyPromptResult>> SearchPastDailyPrompts(string userId, string query)
        {
            try
            {
                _logger.LogInformation("Searching past daily prompts for user {UserId}: {Query}", userId, query);
                
                // Search both Weaviate vectors and PostgreSQL
                var promptResults = new List<DailyPromptResult>();
                
                // Search in Weaviate for vectorized past responses
                var weaviateQuery = new
                {
                    query = query,
                    collection = "DailyPromptResponse",
                    filter = new { userId = userId },
                    limit = 5,
                    fields = new[] { "dayNumber", "reflection", "emotions", "timestamp" }
                };

                var vectorResults = await _weaviateService.SearchUserReflections(userId, query, 5);
                
                if (vectorResults != null)
                {
                    foreach (var result in vectorResults)
                    {
                        promptResults.Add(new DailyPromptResult
                        {
                            DayNumber = result.DayNumber,
                            Reflection = result.Reflection ?? "",
                            RelevanceScore = result.RelevanceScore ?? 0f,
                            Date = result.Timestamp ?? DateTime.UtcNow,
                            Emotions = result.Emotions ?? new List<string>()
                        });
                    }
                }

                // Also search in PostgreSQL for exact matches
                using var conn = new Npgsql.NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                
                var cmd = new Npgsql.NpgsqlCommand(@"
                    SELECT gc.id, gc.created_at, cm.message, cm.response, upd.day_number
                    FROM genie_conversations gc
                    JOIN conversation_messages cm ON gc.id = cm.conversation_id
                    LEFT JOIN user_prompt_deliveries upd ON upd.user_id = gc.user_id::uuid 
                        AND DATE(upd.delivered_at) = DATE(gc.created_at)
                    WHERE gc.user_id = $1 
                    AND (cm.message ILIKE $2 OR cm.response ILIKE $2)
                    ORDER BY gc.created_at DESC
                    LIMIT 5", conn);
                
                cmd.Parameters.AddWithValue(userId);
                cmd.Parameters.AddWithValue($"%{query}%");
                
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var dayNumber = reader.IsDBNull(4) ? 0 : reader.GetInt32(4);
                    var message = reader.GetString(2);
                    var response = reader.GetString(3);
                    
                    promptResults.Add(new DailyPromptResult
                    {
                        DayNumber = dayNumber,
                        Reflection = message,
                        AIResponse = response,
                        Date = reader.GetDateTime(1),
                        RelevanceScore = 0.8f // Fixed score for SQL matches
                    });
                }

                // Deduplicate and sort by relevance
                return promptResults
                    .GroupBy(p => p.DayNumber)
                    .Select(g => g.OrderByDescending(p => p.RelevanceScore).First())
                    .OrderByDescending(p => p.RelevanceScore)
                    .Take(5)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching past daily prompts");
                return new List<DailyPromptResult>();
            }
        }

        private string TruncateContent(string content, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(content)) return "";
            if (content.Length <= maxLength) return content;
            
            var truncated = content.Substring(0, maxLength);
            var lastSpace = truncated.LastIndexOf(' ');
            if (lastSpace > 0) truncated = truncated.Substring(0, lastSpace);
            
            return truncated + "...";
        }

        private string DetermineResourceType(string url)
        {
            if (url.Contains("video") || url.Contains("youtube") || url.Contains("vimeo"))
                return "video";
            if (url.Contains("pdf"))
                return "document";
            if (url.Contains("worksheet") || url.Contains("template"))
                return "worksheet";
            if (url.Contains("blog") || url.Contains("article"))
                return "article";
            
            return "resource";
        }

        private List<CurriculumResource> GetFallbackResources(string issue)
        {
            // Provide generic helpful resources when search fails
            return new List<CurriculumResource>
            {
                new CurriculumResource
                {
                    Title = "Two-Brain Business Toolkit",
                    Url = "https://twobrain.com/toolkit",
                    Preview = "Access our comprehensive toolkit with resources for common business challenges...",
                    RelevanceScore = 0.7f,
                    Topics = new List<string> { "business", "leadership", "management" },
                    ResourceType = "toolkit"
                },
                new CurriculumResource
                {
                    Title = "Leadership Emergency Response Guide",
                    Url = "https://twobrain.com/emergency-guide",
                    Preview = "Quick solutions for urgent business situations and crisis management...",
                    RelevanceScore = 0.6f,
                    Topics = new List<string> { "crisis", "emergency", "leadership" },
                    ResourceType = "guide"
                },
                new CurriculumResource
                {
                    Title = "Contact a Mentor",
                    Url = "https://twobrain.com/mentorship",
                    Preview = "Connect with an experienced mentor for personalized guidance...",
                    RelevanceScore = 0.5f,
                    Topics = new List<string> { "mentorship", "support", "guidance" },
                    ResourceType = "contact"
                }
            };
        }
    }

    // Models
    public class CurriculumSearchResponse
    {
        public string Query { get; set; } = "";
        public List<CurriculumResource> Resources { get; set; } = new();
        public int TotalResults => Resources.Count;
    }

    public class CurriculumResource
    {
        public string Title { get; set; } = "";
        public string Url { get; set; } = "";
        public string Preview { get; set; } = "";
        public float RelevanceScore { get; set; }
        public List<string> Topics { get; set; } = new();
        public string ResourceType { get; set; } = "";
    }

    public class DailyPromptResult
    {
        public int DayNumber { get; set; }
        public string Reflection { get; set; } = "";
        public string? AIResponse { get; set; }
        public float RelevanceScore { get; set; }
        public DateTime Date { get; set; }
        public List<string> Emotions { get; set; } = new();
    }
}
