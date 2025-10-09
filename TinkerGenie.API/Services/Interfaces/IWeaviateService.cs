using System.Collections.Generic;
using System.Security.Claims;
using TinkerGenie.API.Services.Models;

namespace TinkerGenie.API.Services.Interfaces
{
    public interface IWeaviateService
    {
        Task<List<string>> SearchRelevantContentAsync(string query, int limit = 3, ClaimsPrincipal? user = null);
        Task<List<string>> SearchLeadershipContentAsync(string query, int limit = 3, ClaimsPrincipal? user = null);
        Task<List<CurriculumSearchResult>> SearchCurriculumDetailedAsync(string query, int limit = 5);
        Task IndexContentAsync(string content, string type, Dictionary<string, object> metadata, ClaimsPrincipal? user = null);
        Task<bool> TestConnectionAsync();
        Task CreateCollectionAsync(string collectionName);
        Task<List<string>> SearchUserContentAsync(string userId, string query, int limit = 5);
        Task IndexUserContentAsync(string userId, string content, string type, Dictionary<string, object> metadata);
        Task<bool> DeleteUserDataAsync(string userId);
        Task<List<string>> GetUserCollectionsAsync();
        Task<List<UserReflectionResult>> SearchUserReflections(string userId, string query, int limit = 5);
    }

    public class UserReflectionResult
    {
        public int DayNumber { get; set; }
        public string Reflection { get; set; } = string.Empty;
        public DateTime? Timestamp { get; set; }
        public List<string> Emotions { get; set; } = new();
        public float? RelevanceScore { get; set; }
    }
}
