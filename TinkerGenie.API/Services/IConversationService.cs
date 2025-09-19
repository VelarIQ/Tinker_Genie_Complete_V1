using System;
using System.Threading.Tasks;

namespace TinkerGenie.API.Services
{
    public interface IConversationService
    {
        Task<Guid> SaveConversation(string userId, string userMessage, string aiResponse);
        Task<List<ConversationMessage>> GetConversationHistory(string userId, int limit = 10);
    }
    
    public class ConversationMessage
    {
        public string Message { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}
