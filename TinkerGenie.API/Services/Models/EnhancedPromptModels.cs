using System;
using System.Collections.Generic;
using TinkerGenie.API.Services.Models;
using TinkerGenie.API.Models;

namespace TinkerGenie.API.Services.Models
{
    public class Solution
    {
        public string Title { get; set; } = string.Empty;
        public string ActionPlan { get; set; } = string.Empty;
        public string ModuleName { get; set; } = string.Empty;
        public string ModuleLink { get; set; } = string.Empty;
    }

    public class StrategicAction
    {
        public string Description { get; set; } = string.Empty;
        public string ModuleName { get; set; } = string.Empty;
        public string ModuleLink { get; set; } = string.Empty;
        public string PreviousInsight { get; set; } = string.Empty;
        public int? ReferencedDay { get; set; }
    }

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

    public class BurningFireResponse
    {
        public string IssueIdentified { get; set; } = string.Empty;
        public List<Solution> Solutions { get; set; } = new();
        public string QuickWin { get; set; } = string.Empty;
        public List<CurriculumModule> Curriculum { get; set; } = new();
    }

    public class TinkerLevelResponse
    {
        public string StrategicFocus { get; set; } = string.Empty;
        public List<string> DataPoints { get; set; } = new();
        public List<string> ChallengeQuestions { get; set; } = new();
        public List<StrategicAction> StrategicPath { get; set; } = new();
        public string TenXQuestion { get; set; } = string.Empty;
        public string Challenge { get; set; } = string.Empty;
        public List<StrategicAction> StrategicActions { get; set; } = new();
        public List<CurriculumModule> CurriculumRecommendations { get; set; } = new();
        public List<PreviousInsight> PreviousInsights { get; set; } = new();
        public List<IndustryBenchmark> IndustryBenchmarks { get; set; } = new();
    }
}
