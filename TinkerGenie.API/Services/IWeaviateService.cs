using System.Collections.Generic;
using System.Threading.Tasks;
using System.Security.Claims;

namespace TinkerGenie.API.Services
{
    public interface IWeaviateService
    {
        Task<List<string>> SearchRelevantContentAsync(string query, int limit = 3, ClaimsPrincipal? user = null);
        Task<List<string>> SearchLeadershipContentAsync(string query, int limit = 3, ClaimsPrincipal? user = null);
        Task IndexContentAsync(string content, string type, Dictionary<string, object> metadata, ClaimsPrincipal? user = null);
        Task<bool> TestConnectionAsync();
        Task CreateCollectionAsync();
        Task<bool> DeleteUserDataAsync(string userId);
        Task<List<string>> GetUserCollectionsAsync();
        Task<List<string>> SearchDailyPromptsAsync(string query, int limit = 5);
        Task<List<string>> SearchCurriculumAsync(string query, int limit = 5);
        
        // Enhanced search methods for curriculum
        Task<List<CurriculumSearchResult>> SearchCurriculum(string query, int limit = 3);
        Task<List<UserReflectionResult>> SearchUserReflections(string userId, string query, int limit = 5);
    }
    
    // Result models for enhanced search
    public class CurriculumSearchResult
    {
        public string? Title { get; set; }
        public string? Url { get; set; }
        public string? Content { get; set; }
        public List<string>? Topics { get; set; }
        public float? RelevanceScore { get; set; }
    }
    
    public class UserReflectionResult
    {
        public int DayNumber { get; set; }
        public string? Reflection { get; set; }
        public DateTime? Timestamp { get; set; }
        public List<string>? Emotions { get; set; }
        public float? RelevanceScore { get; set; }
    }
}
