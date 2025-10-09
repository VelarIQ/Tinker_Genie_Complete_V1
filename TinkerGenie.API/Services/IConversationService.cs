using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using TinkerGenie.API.Models;

namespace TinkerGenie.API.Services
{
    public interface IConversationService
    {
        Task<Guid> SaveConversation(string userId, string userMessage, string aiResponse);
        Task<List<ConversationMessage>> GetConversationHistory(string userId, int limit = 10);
        
        // Enhanced conversation management
        Task<Guid> CreateConversation(string userId, string title, string conversationType);
        Task<Guid> SaveConversationWithId(Guid conversationId, string userId, string userMessage, string aiResponse);
        Task<bool> UpdateConversationTitle(Guid conversationId, string title);
    }
}
