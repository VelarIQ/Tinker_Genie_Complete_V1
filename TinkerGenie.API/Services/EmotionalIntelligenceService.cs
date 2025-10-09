using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TinkerGenie.API.Services
{
    public interface IEmotionalIntelligenceService
    {
        Task<EmotionalAnalysis> AnalyzeConversationTone(List<string> conversationMessages);
    }

    public class EmotionalIntelligenceService : IEmotionalIntelligenceService
    {
        public Task<EmotionalAnalysis> AnalyzeConversationTone(List<string> conversationMessages)
        {
            if (conversationMessages == null || conversationMessages.Count == 0)
            {
                var emptyAnalysis = new EmotionalAnalysis
                {
                    EmotionalStates = new Dictionary<EmotionalState, double>(),
                    LeadershipEmotionalProfile = new LeadershipEmotionalProfile(),
                    Recommendations = new List<string>()
                };

                return Task.FromResult(emptyAnalysis);
            }

            var analysis = new EmotionalAnalysis 
            {
                EmotionalStates = new Dictionary<EmotionalState, double>
                {
                    { EmotionalState.Hope, CalculateHopeIntensity(conversationMessages) },
                    { EmotionalState.Vulnerability, CalculateVulnerabilityLevel(conversationMessages) },
                    { EmotionalState.Resilience, CalculateResilienceScore(conversationMessages) }
                },
                
                LeadershipEmotionalProfile = new LeadershipEmotionalProfile 
                {
                    SelfAwareness = CalculateSelfAwareness(conversationMessages),
                    EmotionalControl = CalculateEmotionalRegulation(conversationMessages),
                    GrowthMindset = CalculateGrowthOrientation(conversationMessages)
                },

                Recommendations = GenerateEmotionalGuidance(
                    conversationMessages, 
                    GetEmotionalProfile(conversationMessages)
                )
            };

            return Task.FromResult(analysis);
        }

        private double CalculateHopeIntensity(List<string> messages)
        {
            var hopeIndicators = new[] 
            {
                "I want to", "I'm going to", "I believe", "I can", 
                "I'm working towards", "I'm committed to"
            };

            return (double)messages.Count(msg => 
                hopeIndicators.Any(indicator => 
                    msg.ToLower().Contains(indicator))) / messages.Count;
        }

        private double CalculateVulnerabilityLevel(List<string> messages)
        {
            var vulnerabilityIndicators = new[]
            {
                "I'm struggling", "I'm not sure", "I'm afraid", 
                "I'm uncertain", "I'm worried"
            };

            return (double)messages.Count(msg => 
                vulnerabilityIndicators.Any(indicator => 
                    msg.ToLower().Contains(indicator))) / messages.Count;
        }

        private double CalculateResilienceScore(List<string> messages)
        {
            var resilienceIndicators = new[]
            {
                "I'll try again", "I can learn", "I'll improve", 
                "I'm adapting", "I'm growing"
            };

            return (double)messages.Count(msg => 
                resilienceIndicators.Any(indicator => 
                    msg.ToLower().Contains(indicator))) / messages.Count;
        }

        private double CalculateSelfAwareness(List<string> messages)
        {
            var selfAwarenessIndicators = new[]
            {
                "I realize", "I understand now", "I see that", 
                "I'm learning about myself", "I recognize"
            };

            return (double)messages.Count(msg => 
                selfAwarenessIndicators.Any(indicator => 
                    msg.ToLower().Contains(indicator))) / messages.Count;
        }

        private double CalculateEmotionalRegulation(List<string> messages)
        {
            var emotionalControlIndicators = new[]
            {
                "I'm managing", "I'm staying calm", "I'm processing", 
                "I'm taking a step back", "I'm reflecting"
            };

            return (double)messages.Count(msg => 
                emotionalControlIndicators.Any(indicator => 
                    msg.ToLower().Contains(indicator))) / messages.Count;
        }

        private double CalculateGrowthOrientation(List<string> messages)
        {
            var growthIndicators = new[]
            {
                "I want to improve", "I'm developing", "I'm learning", 
                "I'm growing", "I'm expanding my skills"
            };

            return (double)messages.Count(msg => 
                growthIndicators.Any(indicator => 
                    msg.ToLower().Contains(indicator))) / messages.Count;
        }

        private List<string> GenerateEmotionalGuidance(
            List<string> messages, 
            EmotionalProfile profile)
        {
            var recommendations = new List<string>();

            if (profile.HopeLevel < 0.3)
            {
                recommendations.Add(
                    "I notice you're struggling to see a path forward. " +
                    "Let's break down your challenge into small, actionable steps. " +
                    "Remember, progress isn't about perfection, it's about movement."
                );
            }

            if (profile.VulnerabilityLevel > 0.7)
            {
                recommendations.Add(
                    "Your openness is a strength. By acknowledging challenges, " +
                    "you're already taking the first step towards growth. " +
                    "Let's transform these moments of uncertainty into opportunities."
                );
            }

            return recommendations;
        }

        private EmotionalProfile GetEmotionalProfile(List<string> messages)
        {
            return new EmotionalProfile
            {
                HopeLevel = CalculateHopeIntensity(messages),
                VulnerabilityLevel = CalculateVulnerabilityLevel(messages)
            };
        }
    }

    public enum EmotionalState 
    {
        Hope,
        Vulnerability, 
        Resilience,
        Uncertainty,
        Determination
    }

    public class EmotionalAnalysis 
    {
        public Dictionary<EmotionalState, double> EmotionalStates { get; set; } = new();
        public LeadershipEmotionalProfile LeadershipEmotionalProfile { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();
    }

    public class LeadershipEmotionalProfile 
    {
        public double SelfAwareness { get; set; }
        public double EmotionalControl { get; set; }
        public double GrowthMindset { get; set; }
    }

    public class EmotionalProfile
    {
        public double HopeLevel { get; set; }
        public double VulnerabilityLevel { get; set; }
    }
}

