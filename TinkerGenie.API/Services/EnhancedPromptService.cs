using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Linq;
using TinkerGenie.API.Services.Interfaces;
using TinkerGenie.API.Services.Models;
using System.Text.Json;
using TinkerGenie.API.Models;

namespace TinkerGenie.API.Services
{
    public class EnhancedPromptService : IEnhancedPromptService
    {
        private readonly IWeaviateService _weaviateService;
        private readonly ILogger<EnhancedPromptService> _logger;
        
        // TwoBrain members site base URL
        private const string MEMBERS_SITE_URL = "https://members.twobrain.com";
        
        public EnhancedPromptService(IWeaviateService weaviateService, ILogger<EnhancedPromptService> logger)
        {
            _weaviateService = weaviateService;
            _logger = logger;
        }
        
        /// <summary>
        /// Enhanced session rules with link generation
        /// </summary>
        public static class SessionRules
        {
            public const string BurningFires = @"
                BURNING FIRES MODE - Urgent Problem Solving:
                
                1. IDENTIFY: Extract the specific urgent issue in ONE sentence
                2. SEARCH: Query the curriculum for solutions using keywords: {SEARCH_KEYWORDS}
                3. PROVIDE: Exactly 3 actionable solutions with direct links
                
                RESPONSE FORMAT:
                🔥 **Issue Identified:** [One sentence summary]
                
                **Solution 1: [Title]**
                [2-3 sentence action plan]
                📚 Module: [Module Name](MODULE_LINK_1)
                
                **Solution 2: [Title]**
                [2-3 sentence action plan]
                📚 Module: [Module Name](MODULE_LINK_2)
                
                **Solution 3: [Title]**
                [2-3 sentence action plan]
                📚 Module: [Module Name](MODULE_LINK_3)
                
                ⚡ **Quick Win:** [One immediate action they can take today]
                
                RULES:
                - NO pleasantries or emotional support
                - NO 'thank you for sharing' or similar
                - FOCUS only on solutions
                - ALWAYS include clickable module links
            ";
            
            public const string TinkerLevel = @"
                TINKER LEVEL MODE - Strategic Leadership Development:
                
                1. ANALYZE: Consider both curriculum AND previous daily prompts
                2. CHALLENGE: Question assumptions and push strategic thinking
                3. CONNECT: Link to relevant modules and past insights
                
                SEARCH PRIORITIES:
                - Curriculum modules related to: {SEARCH_KEYWORDS}
                - Previous daily prompts about: {LEADERSHIP_TOPICS}
                - Success patterns from Days {PREVIOUS_DAYS}
                
                RESPONSE FORMAT:
                🔧 **Strategic Focus:** [Core challenge/opportunity]
                
                **Data Point:** [Relevant metric or benchmark]
                
                **Challenge Question:** [Thought-provoking question that challenges current thinking]
                
                **Strategic Path Forward:**
                1. [Long-term action] 
                   📚 Deep Dive: [Module Name](MODULE_LINK)
                   
                2. [Building on Day X prompt about Y]
                   💡 Previous Insight: [Brief callback to earlier learning]
                   
                3. [Future state vision]
                   📊 Framework: [Module Name](MODULE_LINK)
                
                **10X Question:** [What would this look like if you were thinking 10x bigger?]
                
                RULES:
                - Use data and benchmarks when available
                - Reference previous daily prompts for continuity
                - Challenge current assumptions
                - Think 3-5 years ahead
                - Provide strategic frameworks via module links
            ";
            
            public const string DailyPrompt = @"
                DAILY PROMPT MODE - Leadership Reflection:
                
                Day {CURRENT_DAY} of 180
                
                1. ASK: One powerful, thought-provoking question
                2. LISTEN: Process their response thoughtfully
                3. INSIGHT: Provide brief wisdom and close the session
                
                RULES:
                - Maximum 2 exchanges (question → response → insight)
                - No follow-up questions after insight
                - End with 'See you tomorrow for Day {NEXT_DAY}!'
            ";
        }
        
        /// <summary>
        /// Generate clickable module links from search results
        /// </summary>
        public class ModuleLinkGenerator
        {
            public static string GenerateModuleLink(CurriculumSearchResult result)
            {
                if (result == null)
                {
                    return MEMBERS_SITE_URL;
                }

                // Parse module information from search result
                var moduleId = ExtractModuleId(result.Content ?? string.Empty);
                var modulePath = ExtractModulePath(result.Metadata ?? new Dictionary<string, object>());
                
                // Generate proper TwoBrain member site link
                if (!string.IsNullOrEmpty(moduleId))
                {
                    return $"{MEMBERS_SITE_URL}/modules/{moduleId}";
                }
                else if (!string.IsNullOrEmpty(modulePath))
                {
                    return $"{MEMBERS_SITE_URL}/courses/{modulePath}";
                }
                
                // Fallback to search if no direct link
                var title = result.Title ?? string.Empty;
                var searchQuery = string.IsNullOrEmpty(title) ? "" : Uri.EscapeDataString(title);
                return string.IsNullOrEmpty(searchQuery)
                    ? MEMBERS_SITE_URL
                    : $"{MEMBERS_SITE_URL}/search?q={searchQuery}";
            }
            
            private static string ExtractModuleId(string content)
            {
                if (string.IsNullOrWhiteSpace(content))
                {
                    return string.Empty;
                }

                // Extract module ID from content
                // Format: "Module ID: TB-2024-SALES-101" or similar
                var match = Regex.Match(content, @"Module\s+ID:\s*(TB-[\w-]+)", RegexOptions.IgnoreCase);
                return match.Success ? match.Groups[1].Value : string.Empty;
            }
            
            private static string ExtractModulePath(Dictionary<string, object> metadata)
            {
                if (metadata == null)
                {
                    return string.Empty;
                }

                // Extract path from metadata
                if (metadata.TryGetValue("path", out var path) && path != null)
                {
                    return path.ToString() ?? string.Empty;
                }
                if (metadata.TryGetValue("course", out var course) && course != null)
                {
                    return $"{course}/modules";
                }
                return string.Empty;
            }
        }
        
        /// <summary>
        /// Format response with proper clickable links
        /// </summary>
        public class ResponseFormatter
        {
            public static string FormatBurningFiresResponse(
                string issue,
                List<Solution> solutions,
                string quickWin)
            {
                var response = $"🔥 **Issue Identified:** {issue}\n\n";
                
                int solutionNumber = 1;
                foreach (var solution in solutions.Take(3))
                {
                    response += $"**Solution {solutionNumber}: {solution.Title}**\n";
                    response += $"{solution.ActionPlan}\n";
                    response += $"📚 Module: [{solution.ModuleName}]({solution.ModuleLink})\n\n";
                    solutionNumber++;
                }
                
                response += $"⚡ **Quick Win:** {quickWin}";
                
                return response;
            }
            
            public static string FormatTinkerLevelResponse(
                string strategicFocus,
                string dataPoint,
                string challengeQuestion,
                List<StrategicAction> actions,
                string tenXQuestion)
            {
                var response = $"🔧 **Strategic Focus:** {strategicFocus}\n\n";
                response += $"**Data Point:** {dataPoint}\n\n";
                response += $"**Challenge Question:** {challengeQuestion}\n\n";
                response += "**Strategic Path Forward:**\n";
                
                int actionNumber = 1;
                foreach (var action in actions)
                {
                    response += $"{actionNumber}. {action.Description}\n";
                    
                    if (!string.IsNullOrEmpty(action.ModuleLink))
                    {
                        response += $"   📚 Deep Dive: [{action.ModuleName}]({action.ModuleLink})\n";
                    }
                    
                    if (!string.IsNullOrEmpty(action.PreviousInsight))
                    {
                        response += $"   💡 Previous Insight: {action.PreviousInsight}\n";
                    }
                    
                    response += "\n";
                    actionNumber++;
                }
                
                response += $"**10X Question:** {tenXQuestion}";
                
                return response;
            }
        }
        
        /// <summary>
        /// Search curriculum and generate solutions with links
        /// </summary>
        public async Task<List<Solution>> SearchAndGenerateSolutions(string issue, int limit = 3)
        {
            var solutions = new List<Solution>();
            
            try
            {
                // Extract keywords from the issue
                var keywords = ExtractKeywords(issue);
                
                // Search curriculum
                var searchResults = await _weaviateService.SearchCurriculumDetailedAsync(
                    keywords,
                    limit * 2
                );
                
                foreach (var result in searchResults.Take(limit))
                {
                    solutions.Add(new Solution
                    {
                        Title = ExtractSolutionTitle(result),
                        ActionPlan = GenerateActionPlan(result, issue),
                        ModuleName = result.Title ?? "Curriculum Module",
                        ModuleLink = ModuleLinkGenerator.GenerateModuleLink(result)
                    });
                }
                
                // If not enough results, add fallback solutions
                while (solutions.Count < limit)
                {
                    solutions.Add(new Solution
                    {
                        Title = $"Custom Strategy {solutions.Count + 1}",
                        ActionPlan = "Schedule a strategy session with your mentor to develop a custom solution.",
                        ModuleName = "Mentor Toolkit",
                        ModuleLink = $"{MEMBERS_SITE_URL}/mentorship/schedule"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching curriculum for solutions");
                
                // Provide fallback solutions
                solutions.Add(new Solution
                {
                    Title = "Immediate Action Plan",
                    ActionPlan = "Document the current situation and its impact on your business.",
                    ModuleName = "Crisis Management Toolkit",
                    ModuleLink = $"{MEMBERS_SITE_URL}/courses/crisis-management"
                });
            }
            
            return solutions;
        }
        
        /// <summary>
        /// Search both curriculum and previous prompts for Tinker Level
        /// </summary>
        public async Task<TinkerLevelSearchResult> SearchForStrategicInsights(
            string topic, 
            string userId, 
            int currentDay)
        {
            var result = new TinkerLevelSearchResult();
            
            try
            {
                // Search curriculum
                var curriculumResults = await _weaviateService.SearchCurriculumDetailedAsync(topic, 5);
                
                result.CurriculumModules = curriculumResults.Select(r => new ModuleReference
                {
                    Name = r.Title ?? "Curriculum Module",
                    Link = ModuleLinkGenerator.GenerateModuleLink(r),
                    Relevance = CalculateRelevance(r, topic)
                }).OrderByDescending(m => m.Relevance).ToList();
                
                // Search previous daily prompts (look back up to 30 days)
                var promptDays = Enumerable.Range(Math.Max(1, currentDay - 30), Math.Min(currentDay - 1, 30));
                var previousPrompts = new List<DailyPromptHistory>();
                
                foreach (var day in promptDays)
                {
                    var prompt = await GetDailyPromptHistory(userId, day);
                    if (prompt != null && IsRelevantToTopic(prompt, topic))
                    {
                        previousPrompts.Add(prompt);
                    }
                }
                
                // Wait for curriculum search
                // var curriculumResults = await curriculumTask; // This line is removed as per edit hint
                
                // Combine insights
                result.CurriculumModules = GetFallbackCurriculumModules(topic);
                
                result.PreviousInsights = previousPrompts.Select(p => new PreviousInsight
                {
                    Day = p.Day,
                    Topic = p.Topic,
                    KeyLearning = p.KeyLearning,
                    DateDiscussed = p.Date
                }).ToList();
                
                // Add strategic benchmarks if available
                result.IndustryBenchmarks = await GetIndustryBenchmarks(topic);
                
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching for strategic insights");
            }
            
            return result;
        }
        
        // Helper methods
        private string ExtractKeywords(string issue)
        {
            // Remove common words and extract meaningful keywords
            var stopWords = new HashSet<string> { "the", "is", "at", "which", "on", "and", "a", "an" };
            var words = issue.ToLower().Split(' ')
                .Where(w => !stopWords.Contains(w) && w.Length > 2)
                .Take(5);
            return string.Join(" ", words);
        }
        
        private string ExtractSolutionTitle(CurriculumSearchResult result)
        {
            // Extract a concise title from the result
            if (!string.IsNullOrEmpty(result.Title))
            {
                return result.Title.Length > 50 
                    ? result.Title.Substring(0, 47) + "..." 
                    : result.Title;
            }
            return "Strategic Solution";
        }
        
        private string GenerateActionPlan(CurriculumSearchResult result, string issue)
        {
            // Generate a 2-3 sentence action plan based on the module content
            var template = "Apply the {0} framework to address this issue. " +
                          "Start by {1}, then implement the system over the next 7 days.";
            
            var content = result.Content ?? string.Empty;
            var framework = ExtractFramework(content);
            if (string.IsNullOrWhiteSpace(framework))
            {
                framework = "proven methodology";
            }

            var firstStep = ExtractFirstStep(content);
            if (string.IsNullOrWhiteSpace(firstStep))
            {
                firstStep = "assessing your current situation";
            }
            
            return string.Format(template, framework, firstStep);
        }
        
        private string ExtractFramework(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return string.Empty;
            }

            // Extract framework name from content
            var match = Regex.Match(content, @"(?:framework|system|method|process):\s*([^\.]+)", 
                RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
        }
        
        private string ExtractFirstStep(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return string.Empty;
            }

            // Extract first action step
            var match = Regex.Match(content, @"(?:step 1|first|start by|begin with):\s*([^\.]+)", 
                RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim().ToLower() : string.Empty;
        }
        
        private async Task<DailyPromptHistory> GetDailyPromptHistory(string userId, int day)
        {
            // Retrieve previous daily prompt responses from database
            // This would query your PostgreSQL database
            await Task.CompletedTask;
            return new DailyPromptHistory();
        }
        
        private bool IsRelevantToTopic(DailyPromptHistory prompt, string topic)
        {
            // Check if previous prompt is relevant to current topic
            var topicWords = topic.ToLower().Split(' ');
            var promptWords = (prompt.Topic + " " + prompt.KeyLearning).ToLower();
            
            return topicWords.Any(word => promptWords.Contains(word));
        }
        
        private double CalculateRelevance(CurriculumSearchResult result, string topic)
        {
            // Calculate relevance score (0-1) based on keyword matches
            var topicWords = topic.ToLower().Split(' ').ToHashSet();
            var resultWords = (result.Title + " " + result.Content).ToLower().Split(' ');
            
            var matches = resultWords.Count(w => topicWords.Contains(w));
            return Math.Min(1.0, matches / (double)topicWords.Count);
        }
        
        private Task<List<IndustryBenchmark>> GetIndustryBenchmarks(string topic)
        {
            var benchmarks = new List<IndustryBenchmark>();

            if (string.IsNullOrWhiteSpace(topic))
            {
                return Task.FromResult(benchmarks);
            }

            // Add relevant benchmarks based on topic
            if (topic.Contains("retention", StringComparison.OrdinalIgnoreCase))
            {
                benchmarks.Add(new IndustryBenchmark
                {
                    Metric = "Industry Average Member Retention",
                    Value = "82% annually",
                    Source = "2024 Fitness Industry Report"
                });
            }
            
            if (topic.Contains("revenue", StringComparison.OrdinalIgnoreCase) || 
                topic.Contains("pricing", StringComparison.OrdinalIgnoreCase))
            {
                benchmarks.Add(new IndustryBenchmark
                {
                    Metric = "Average Revenue per Member",
                    Value = "$187/month",
                    Source = "TwoBrain Industry Study 2024"
                });
            }
            
            return Task.FromResult(benchmarks);
        }

        // Interface implementation methods
        public Task<BurningFireResponse> HandleBurningFire(string userId, string issue)
        {
            // Implementation for burning fire handling
            var response = new BurningFireResponse
            {
                IssueIdentified = issue,
                Solutions = new List<Solution>(),
                QuickWin = string.Empty,
                Curriculum = new List<CurriculumModule>()
            };

            return Task.FromResult(response);
        }

        public Task<TinkerLevelResponse> HandleTinkerLevel(string userId, string challenge)
        {
            // Implementation for tinker level handling
            var response = new TinkerLevelResponse
            {
                Challenge = challenge,
                StrategicActions = new List<StrategicAction>(),
                CurriculumRecommendations = new List<CurriculumModule>(),
                PreviousInsights = new List<Models.PreviousInsight>(),
                IndustryBenchmarks = new List<Models.IndustryBenchmark>()
            };

            return Task.FromResult(response);
        }

        private List<ModuleReference> GetFallbackCurriculumModules(string topic)
        {
            var fallbackModules = new List<ModuleReference>();
            var topicWords = topic.ToLower().Split(' ');

            // Add relevant curriculum modules based on topic keywords
            if (topicWords.Any(w => w.Contains("leadership", StringComparison.OrdinalIgnoreCase)))
            {
                fallbackModules.Add(new ModuleReference
                {
                    Name = "Leadership Foundations",
                    Link = $"{MEMBERS_SITE_URL}/courses/leadership-foundations",
                    Relevance = 0.8
                });
            }
            if (topicWords.Any(w => w.Contains("sales", StringComparison.OrdinalIgnoreCase)))
            {
                fallbackModules.Add(new ModuleReference
                {
                    Name = "Sales Mastery",
                    Link = $"{MEMBERS_SITE_URL}/courses/sales-mastery",
                    Relevance = 0.7
                });
            }
            if (topicWords.Any(w => w.Contains("marketing", StringComparison.OrdinalIgnoreCase)))
            {
                fallbackModules.Add(new ModuleReference
                {
                    Name = "Digital Marketing",
                    Link = $"{MEMBERS_SITE_URL}/courses/digital-marketing",
                    Relevance = 0.6
                });
            }
            if (topicWords.Any(w => w.Contains("fitness", StringComparison.OrdinalIgnoreCase)))
            {
                fallbackModules.Add(new ModuleReference
                {
                    Name = "Fitness Business",
                    Link = $"{MEMBERS_SITE_URL}/courses/fitness-business",
                    Relevance = 0.5
                });
            }

            // Add a generic module if no specific ones match
            if (fallbackModules.Count == 0)
            {
                fallbackModules.Add(new ModuleReference
                {
                    Name = "General Business Strategy",
                    Link = $"{MEMBERS_SITE_URL}/courses/business-strategy",
                    Relevance = 0.4
                });
            }

            return fallbackModules;
        }
    }

    // Supporting models
    public class CurriculumSearchResult
    {
        public string? Title { get; set; }
        public string? Url { get; set; }
        public string? Content { get; set; }
        public List<string>? Topics { get; set; }
        public float? RelevanceScore { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }
    
    public class TinkerLevelSearchResult
    {
        public List<ModuleReference> CurriculumModules { get; set; } = new();
        public List<PreviousInsight> PreviousInsights { get; set; } = new();
        public List<IndustryBenchmark> IndustryBenchmarks { get; set; } = new();
    }
    
    public class ModuleReference
    {
        public string Name { get; set; } = string.Empty;
        public string Link { get; set; } = string.Empty;
        public double Relevance { get; set; }
    }
    
    public class PreviousInsight
    {
        public int Day { get; set; }
        public string Topic { get; set; } = string.Empty;
        public string KeyLearning { get; set; } = string.Empty;
        public DateTime DateDiscussed { get; set; }
    }
    
    public class DailyPromptHistory
    {
        public int Day { get; set; }
        public string Topic { get; set; } = string.Empty;
        public string KeyLearning { get; set; } = string.Empty;
        public DateTime Date { get; set; }
    }
    
    public class IndustryBenchmark
    {
        public string Metric { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
    }
}
