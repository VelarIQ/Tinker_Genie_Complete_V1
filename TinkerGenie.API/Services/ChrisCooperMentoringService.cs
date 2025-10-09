using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Npgsql;
using TinkerGenie.API.Services.Interfaces;

namespace TinkerGenie.API.Services
{
    public interface IChrisCooperMentoringService
    {
        Task<MentoringGuidance> GenerateMentoringInsights(
            string userId, 
            string userInput, 
            List<SearchResult>? searchResults = null);
    }

    public class ChrisCooperMentoringService : IChrisCooperMentoringService
    {
        private readonly string _connectionString;
        private readonly IWeaviateService _weaviateService;

        public ChrisCooperMentoringService(
            IConfiguration configuration, 
            IWeaviateService weaviateService)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? throw new ArgumentNullException(nameof(configuration), "Connection string cannot be null");
            _weaviateService = weaviateService ?? throw new ArgumentNullException(nameof(weaviateService), "Weaviate service cannot be null");
        }

        public async Task<MentoringGuidance> GenerateMentoringInsights(
            string userId, 
            string userInput, 
            List<SearchResult>? searchResults = null)
        {
            // If no search results provided, perform a search
            searchResults ??= (await _weaviateService.SearchRelevantContentAsync(userInput, limit: 5))
                .Select(r => new SearchResult { Title = r, Content = string.Empty, Relevance = 0.5, Link = string.Empty })
                .ToList();

            // Retrieve user context
            var userContext = await RetrieveUserContext(userId);

            return new MentoringGuidance 
            {
                // Core Mentoring Principles from Chris Cooper's Prompt
                MentoringApproach = new MentoringPrinciples 
                {
                    // "Hope is not passive"
                    HopeFramework = "Transforming hope into actionable momentum",
                    
                    // "Decision rooted in honesty, agency, and a path forward"
                    DecisionMakingGuidance = GenerateDecisionPathway(userInput, searchResults ?? new List<SearchResult>()),
                    
                    // "Encourage users to look in the mirror"
                    SelfReflectionPrompts = GenerateSelfReflectionQuestions(userInput, userContext),
                    
                    // "Acknowledge the gap between where they are and where they want to be"
                    GapAnalysisInsights = AnalyzePersonalGrowthGap(userContext, userInput)
                },

                // Actionable Recommendations
                NextSteps = GenerateActionableRecommendations(
                    userId, 
                    userInput, 
                    searchResults ?? new List<SearchResult>(), 
                    userContext
                ),

                // Curriculum-based Insights
                CurriculumRecommendations = (searchResults ?? new List<SearchResult>())
                    .OrderByDescending(r => r.Relevance)
                    .Take(3)
                    .Select(r => new CurriculumRecommendation
                    {
                        Title = r.Title,
                        Link = r.Link,
                        RelevanceReason = GenerateRelevanceExplanation(r, userInput)
                    })
                    .ToList()
            };
        }

        private List<string> GenerateDecisionPathway(
            string userInput, 
            List<SearchResult> searchResults)
        {
            var topResult = searchResults.OrderByDescending(r => r.Relevance).FirstOrDefault();

            return new List<string> 
            {
                $"Let's break down your challenge: {userInput}",
                topResult != null 
                    ? $"Insight from our curriculum: {topResult.Title}"
                    : "Let's explore potential strategies",
                "What specific action can you take in the next 24 hours?",
                "How does this step align with your larger vision?"
            };
        }

        private List<string> GenerateSelfReflectionQuestions(
            string userInput, 
            UserContext userContext)
        {
            return new List<string> 
            {
                "What does this challenge reveal about your current approach?",
                $"Reflecting on your business '{userContext.BusinessName}', how does this issue impact your core mission?",
                "If you were advising a friend in this situation, what would you say?",
                "What strengths can you leverage to overcome this?"
            };
        }

        private List<string> AnalyzePersonalGrowthGap(
            UserContext userContext, 
            string userInput)
        {
            var insights = new List<string>();

            // Analyze gap between current state and desired state
            if (userContext.CurrentSkillLevel < 0.5)
            {
                insights.Add(
                    "I notice you're in the early stages of developing this skill. " +
                    "This challenge is an opportunity for significant growth."
                );
            }

            // Contextual gap analysis based on user input
            insights.Add(
                $"Your challenge '{userInput}' suggests a gap in " +
                "strategic thinking or execution. Let's identify the specific barriers."
            );

            return insights;
        }

        private List<string> GenerateActionableRecommendations(
            string userId, 
            string userInput, 
            List<SearchResult> searchResults,
            UserContext userContext)
        {
            var recommendations = new List<string>();

            // Curriculum-based recommendation
            var topResult = searchResults.OrderByDescending(r => r.Relevance).FirstOrDefault();
            if (topResult != null)
            {
                recommendations.Add(
                    $"Based on our curriculum, I recommend exploring: {topResult.Title}. " +
                    $"This resource directly addresses your current challenge."
                );
            }

            // Personalized action steps
            recommendations.Add(
                "Break down your challenge into three actionable steps:\n" +
                "1. Identify the core issue\n" +
                "2. List potential approaches\n" +
                "3. Choose one approach to implement this week"
            );

            // Business-specific guidance
            recommendations.Add(
                $"For {userContext.BusinessName}, consider how this challenge " +
                "impacts your broader strategic objectives. What small change " +
                "can create the most significant leverage?"
            );

            return recommendations;
        }

        private string GenerateRelevanceExplanation(
            SearchResult result, 
            string userInput)
        {
            return $"This resource applies to your challenge '{userInput}' because " +
                   $"it addresses key themes of {result.Title}. The insights here " +
                   "can provide strategic guidance and practical approaches.";
        }

        private async Task<UserContext> RetrieveUserContext(string userId)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(@"
                SELECT 
                    name, 
                    business_name, 
                    current_day, 
                    (SELECT AVG(skill_level) 
                     FROM user_skill_progression 
                     WHERE user_id = @userId) as current_skill_level
                FROM user_data
                WHERE id = @userId", conn);

            cmd.Parameters.AddWithValue("userId", Guid.Parse(userId));

            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                throw new Exception($"User not found: {userId}");
            }

            return new UserContext
            {
                Name = reader.GetString(0),
                BusinessName = reader.GetString(1),
                CurrentDay = reader.GetInt32(2),
                CurrentSkillLevel = reader.IsDBNull(3) ? 0 : reader.GetDouble(3)
            };
        }
    }

    public class MentoringGuidance 
    {
        public MentoringPrinciples MentoringApproach { get; set; } = new();
        public List<string> NextSteps { get; set; } = new();
        public List<CurriculumRecommendation> CurriculumRecommendations { get; set; } = new();
    }

    public class MentoringPrinciples 
    {
        public string HopeFramework { get; set; } = string.Empty;
        public List<string> DecisionMakingGuidance { get; set; } = new();
        public List<string> SelfReflectionPrompts { get; set; } = new();
        public List<string> GapAnalysisInsights { get; set; } = new();
    }

    public class CurriculumRecommendation
    {
        public string Title { get; set; } = string.Empty;
        public string Link { get; set; } = string.Empty;
        public string RelevanceReason { get; set; } = string.Empty;
    }

    public class UserContext
    {
        public string Name { get; set; } = string.Empty;
        public string BusinessName { get; set; } = string.Empty;
        public int CurrentDay { get; set; }
        public double CurrentSkillLevel { get; set; }
    }

    public class SearchResult
    {
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public double Relevance { get; set; }
        public string Link { get; set; } = string.Empty;
    }
}

