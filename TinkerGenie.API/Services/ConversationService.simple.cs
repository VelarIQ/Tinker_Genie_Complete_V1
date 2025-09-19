using TinkerGenie.API.Models;

namespace TinkerGenie.API.Services
{
    public class ConversationService
    {
        private readonly ILogger<ConversationService> _logger;

        public ConversationService(ILogger<ConversationService> logger)
        {
            _logger = logger;
        }

        public async Task<Guid> SaveConversation(string userId, string userMessage, string aiResponse)
        {
            // Simplified version - just log for now
            _logger.LogInformation($"Saving conversation for user {userId}");
            return await Task.FromResult(Guid.NewGuid());
        }

        public async Task<List<ConversationMessage>> GetConversationHistory(string userId, int limit = 10)
        {
            // Return empty history for now
            return await Task.FromResult(new List<ConversationMessage>());
        }
    }
}

