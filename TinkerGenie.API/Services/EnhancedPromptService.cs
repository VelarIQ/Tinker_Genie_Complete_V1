using System.Text;
using System.Text.Json;
using Npgsql;

namespace TinkerGenie.API.Services
{
    public class EnhancedPromptService : IEnhancedPromptService
    {
        private readonly ILogger<EnhancedPromptService> _logger;
        private readonly IWeaviateService? _weaviateService;
        private readonly string _connectionString;

        // TwoBrain module links
        private readonly Dictionary<string, string> _moduleLinks = new()
        {
            { "crisis-staffing", "https://members.twobrain.com/modules/TB-2024-CRISIS-STAFF" },
            { "emergency-protocols", "https://members.twobrain.com/modules/emergency-response-guide" },
            { "retention-emergency", "https://members.twobrain.com/modules/retention-emergency" },
            { "premium-tiers", "https://members.twobrain.com/modules/premium-tier-design" },
            { "hiring-playbook", "https://members.twobrain.com/modules/hiring-a-players" },
            { "staff-development", "https://members.twobrain.com/modules/staff-career-paths" },
            { "metrics-dashboard", "https://members.twobrain.com/modules/key-metrics-tracking" },
            { "systems-automation", "https://members.twobrain.com/modules/gym-operating-system" },
            { "lead-generation", "https://members.twobrain.com/modules/lead-magnet-mastery" },
            { "sales-conversations", "https://members.twobrain.com/modules/no-sweat-intro" }
        };

        public EnhancedPromptService(
            ILogger<EnhancedPromptService> logger,
            IConfiguration configuration,
            IWeaviateService? weaviateService = null)
        {
            _logger = logger;
            _weaviateService = weaviateService;
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
        }

        public async Task<BurningFireResponse> HandleBurningFire(string userId, string issue)
        {
            var response = new BurningFireResponse
            {
                IssueIdentified = $"🔥 Issue Identified: {ExtractIssue(issue)}",
                Solutions = new List<Solution>
                {
                    new Solution
                    {
                        Title = "Immediate Staffing Protocol",
                        Description = "Activate your emergency coverage plan right now",
                        ModuleLink = _moduleLinks["crisis-staffing"],
                        ModuleName = "Crisis Staffing Guide"
                    },
                    new Solution
                    {
                        Title = "Client Communication",
                        Description = "Send immediate notification to affected members",
                        ModuleLink = _moduleLinks["retention-emergency"],
                        ModuleName = "Retention Emergency"
                    },
                    new Solution
                    {
                        Title = "Long-term Prevention",
                        Description = "Build redundancy into your staffing model",
                        ModuleLink = _moduleLinks["hiring-playbook"],
                        ModuleName = "Hiring A-Players"
                    }
                },
                QuickWin = "⚡ Quick Win: Message your team RIGHT NOW about coverage. Transparency in the next 5 minutes prevents panic."
            };

            // Format the complete response with clickable links
            var sb = new StringBuilder();
            sb.AppendLine(response.IssueIdentified);
            sb.AppendLine();
            
            int solutionNum = 1;
            foreach (var solution in response.Solutions)
            {
                sb.AppendLine($"**Solution {solutionNum}: {solution.Title}**");
                sb.AppendLine(solution.Description);
                sb.AppendLine($"📚 Module: [{solution.ModuleName}]({solution.ModuleLink})");
                sb.AppendLine();
                solutionNum++;
            }
            
            sb.AppendLine(response.QuickWin);
            
            response.FormattedResponse = sb.ToString();
            return response;
        }

        public async Task<TinkerLevelResponse> HandleTinkerLevel(string userId, string challenge)
        {
            var response = new TinkerLevelResponse
            {
                StrategicFocus = $"🔧 Strategic Focus: {ExtractChallenge(challenge)}",
                DataPoints = GetRelevantDataPoints(challenge),
                ChallengeQuestions = new List<string>
                {
                    "❓ What assumption about this might be completely wrong?",
                    "🎯 If you could only change ONE thing, what creates the biggest impact?",
                    "🚀 What would this look like if it were easy?"
                },
                StrategicPath = new List<StrategicAction>
                {
                    new StrategicAction
                    {
                        Step = 1,
                        Title = "Implement Premium Tier Framework",
                        Description = "Start this week with your top 10 clients",
                        ModuleLink = _moduleLinks["premium-tiers"]
                    },
                    new StrategicAction
                    {
                        Step = 2,
                        Title = "Automate Key Processes",
                        Description = "Free up 5 hours per week",
                        ModuleLink = _moduleLinks["systems-automation"]
                    },
                    new StrategicAction
                    {
                        Step = 3,
                        Title = "Track & Scale",
                        Description = "Measure what matters and double down",
                        ModuleLink = _moduleLinks["metrics-dashboard"]
                    }
                },
                TenXQuestion = "🚀 10X Question: How would you solve this in 1/10th the time with 10X the impact?"
            };

            // Format the complete response
            var sb = new StringBuilder();
            sb.AppendLine(response.StrategicFocus);
            sb.AppendLine();
            
            foreach (var dataPoint in response.DataPoints)
            {
                sb.AppendLine(dataPoint);
            }
            sb.AppendLine();
            
            foreach (var question in response.ChallengeQuestions)
            {
                sb.AppendLine(question);
            }
            sb.AppendLine();
            
            sb.AppendLine("**Strategic Path:**");
            foreach (var action in response.StrategicPath)
            {
                sb.AppendLine($"{action.Step}. {action.Title}");
                sb.AppendLine($"   {action.Description}");
                sb.AppendLine($"   📚 Deep Dive: [{GetModuleName(action.ModuleLink)}]({action.ModuleLink})");
                sb.AppendLine();
            }
            
            sb.AppendLine(response.TenXQuestion);
            
            response.FormattedResponse = sb.ToString();
            return response;
        }

        private string ExtractIssue(string message)
        {
            // Extract the core issue from the message
            var issue = message.Replace("burning fire", "", StringComparison.OrdinalIgnoreCase)
                              .Replace("burning fires", "", StringComparison.OrdinalIgnoreCase)
                              .Trim();
            
            if (string.IsNullOrEmpty(issue))
                return "Urgent situation requiring immediate action";
                
            return issue.Length > 100 ? issue.Substring(0, 100) + "..." : issue;
        }

        private string ExtractChallenge(string message)
        {
            var challenge = message.Replace("other tinker level", "", StringComparison.OrdinalIgnoreCase)
                                 .Replace("tinker level", "", StringComparison.OrdinalIgnoreCase)
                                 .Trim();
            
            if (string.IsNullOrEmpty(challenge))
                return "Strategic challenge";
                
            return challenge.Length > 100 ? challenge.Substring(0, 100) + "..." : challenge;
        }

        private List<string> GetRelevantDataPoints(string challenge)
        {
            var dataPoints = new List<string>();
            
            if (challenge.ToLower().Contains("retention") || challenge.ToLower().Contains("client"))
            {
                dataPoints.Add("📊 Industry average retention: 82% monthly");
                dataPoints.Add("📈 Top 10% gyms achieve: 95% monthly retention");
                dataPoints.Add("💰 Each 1% improvement = $8,400/year (150 member gym)");
            }
            else if (challenge.ToLower().Contains("revenue") || challenge.ToLower().Contains("pricing"))
            {
                dataPoints.Add("📊 Average revenue per member: $158/month");
                dataPoints.Add("📈 Top performers: $235+ ARM");
                dataPoints.Add("💰 Premium tier adoption: 15-25% of members");
            }
            else if (challenge.ToLower().Contains("staff") || challenge.ToLower().Contains("hiring"))
            {
                dataPoints.Add("📊 Optimal coach:client ratio: 1:50");
                dataPoints.Add("📈 Staff retention best practice: 18+ months");
                dataPoints.Add("💰 Coach compensation: 35-44% of revenue generated");
            }
            else
            {
                dataPoints.Add("📊 Average gym growth: 8-12% annually");
                dataPoints.Add("📈 Top performers: 20-30% YoY growth");
                dataPoints.Add("💰 Profit margin target: 33% or higher");
            }
            
            return dataPoints;
        }

        private string GetModuleName(string moduleLink)
        {
            // Extract module name from link
            foreach (var kvp in _moduleLinks)
            {
                if (kvp.Value == moduleLink)
                {
                    return kvp.Key.Replace("-", " ").ToUpper();
                }
            }
            return "Module";
        }
    }
}
