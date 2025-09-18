using System;
using System.Threading.Tasks;

namespace TinkerGenie.API.Services
{
    public class MockAIService
    {
        private readonly string[] responses = new[]
        {
            "Hello! I'm currently running in mock mode due to API quota limits. How can I help you with your leadership journey today?",
            "Great question! As a leader, it's important to focus on clear communication and team empowerment.",
            "That's an excellent point. Let me share some insights on effective leadership strategies.",
            "I understand your concern. Leadership challenges require thoughtful approaches and consistent action.",
            "Thank you for sharing that. Building strong teams starts with understanding individual strengths."
        };
        
        private Random random = new Random();
        
        public Task<string> GetResponseAsync(string message)
        {
            // Return a random mock response
            return Task.FromResult(responses[random.Next(responses.Length)]);
        }
    }
}
