using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using TinkerGenie.API.Data;
using TinkerGenie.API.Models;

namespace TinkerGenie.API.Services
{
    public class ConversationService : IConversationService
    {
        private readonly ILogger<ConversationService> _logger;
        private readonly TinkerGenieContext _context;
        
        public ConversationService(ILogger<ConversationService> logger, TinkerGenieContext context)
        {
            _logger = logger;
            _context = context;
        }
        
        public async Task<Guid> SaveConversation(string userId, string userMessage, string aiResponse)
        {
            try
            {
                // Find or create conversation
                var userGuid = Guid.TryParse(userId, out var uid) ? uid : Guid.Empty;
                
                var conversation = await _context.GenieConversations
                    .Where(c => c.UserId == userGuid && c.Status == "active")
                    .OrderByDescending(c => c.LastMessageAt)
                    .FirstOrDefaultAsync();
                
                if (conversation == null)
                {
                    conversation = new GenieConversation
                    {
                        UserId = userGuid,
                        ConversationType = "chat",
                        Status = "active",
                        StartedAt = DateTime.UtcNow
                    };
                    _context.GenieConversations.Add(conversation);
                }
                
                // Add user message
                _context.ConversationMessages.Add(new ConversationMessage
                {
                    ConversationId = conversation.Id,
                    UserId = userGuid,
                    Sender = "user",
                    MessageText = userMessage,
                    Content = userMessage,
                    IsUser = true,
                    CreatedAt = DateTime.UtcNow
                });
                
                // Add AI response
                _context.ConversationMessages.Add(new ConversationMessage
                {
                    ConversationId = conversation.Id,
                    Sender = "genie",
                    MessageText = aiResponse,
                    Content = aiResponse,
                    IsUser = false,
                    AiModelUsed = "gpt-4o-mini",
                    CreatedAt = DateTime.UtcNow
                });
                
                // Update conversation
                conversation.MessageCount += 2;
                conversation.LastMessageAt = DateTime.UtcNow;
                
                await _context.SaveChangesAsync();
                
                _logger.LogInformation($"Saved conversation {conversation.Id} for user {userId}");
                return conversation.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving conversation for user {userId}");
                return Guid.Empty;
            }
        }
        
        public async Task<List<ConversationMessage>> GetConversationHistory(string userId, int limit = 10)
        {
            try
            {
                var userGuid = Guid.TryParse(userId, out var uid) ? uid : Guid.Empty;
                
                // Get the most recent active conversation
                var conversation = await _context.GenieConversations
                    .Where(c => c.UserId == userGuid && c.Status == "active")
                    .OrderByDescending(c => c.LastMessageAt)
                    .FirstOrDefaultAsync();
                
                if (conversation == null)
                {
                    return new List<ConversationMessage>();
                }
                
                // Get recent messages from this conversation
                var messages = await _context.ConversationMessages
                    .Where(m => m.ConversationId == conversation.Id)
                    .OrderByDescending(m => m.CreatedAt)
                    .Take(limit)
                    .OrderBy(m => m.CreatedAt)
                    .ToListAsync();
                
                return messages;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting conversation history for user {userId}");
                return new List<ConversationMessage>();
            }
        }

        public async Task<Guid> CreateConversation(string userId, string title, string conversationType)
        {
            try
            {
                var userGuid = Guid.TryParse(userId, out var uid) ? uid : Guid.Empty;
                
                var conversation = new GenieConversation
                {
                    Id = Guid.NewGuid(),
                    UserId = userGuid,
                    Title = title,
                    ConversationType = conversationType,
                    Status = "active",
                    StartedAt = DateTime.UtcNow,
                    LastMessageAt = DateTime.UtcNow
                };
                
                _context.GenieConversations.Add(conversation);
                await _context.SaveChangesAsync();
                
                _logger.LogInformation($"Created {conversationType} conversation '{title}' ({conversation.Id}) for user {userId}");
                return conversation.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error creating conversation for user {userId}");
                return Guid.Empty;
            }
        }

        public async Task<Guid> SaveConversationWithId(Guid conversationId, string userId, string userMessage, string aiResponse)
        {
            try
            {
                var userGuid = Guid.TryParse(userId, out var uid) ? uid : Guid.Empty;
                
                // Find the specific conversation
                var conversation = await _context.GenieConversations
                    .FirstOrDefaultAsync(c => c.Id == conversationId);
                
                if (conversation == null)
                {
                    _logger.LogWarning($"Conversation {conversationId} not found for user {userId}");
                    return Guid.Empty;
                }
                
                // Add user message
                _context.ConversationMessages.Add(new ConversationMessage
                {
                    ConversationId = conversationId,
                    UserId = userGuid,
                    Sender = "user",
                    MessageText = userMessage,
                    Content = userMessage,
                    IsUser = true,
                    CreatedAt = DateTime.UtcNow
                });
                
                // Add AI response
                _context.ConversationMessages.Add(new ConversationMessage
                {
                    ConversationId = conversationId,
                    Sender = "genie",
                    MessageText = aiResponse,
                    Content = aiResponse,
                    IsUser = false,
                    AiModelUsed = "gpt-4o-mini",
                    CreatedAt = DateTime.UtcNow
                });
                
                // Update conversation
                conversation.MessageCount += 2;
                conversation.LastMessageAt = DateTime.UtcNow;
                
                await _context.SaveChangesAsync();
                
                _logger.LogInformation($"Saved messages to conversation {conversationId} for user {userId}");
                return conversationId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving to conversation {conversationId} for user {userId}");
                return Guid.Empty;
            }
        }

        public async Task<bool> UpdateConversationTitle(Guid conversationId, string title)
        {
            try
            {
                var conversation = await _context.GenieConversations
                    .FirstOrDefaultAsync(c => c.Id == conversationId);
                
                if (conversation == null)
                {
                    _logger.LogWarning($"Conversation {conversationId} not found for title update");
                    return false;
                }
                
                conversation.Title = title;
                await _context.SaveChangesAsync();
                
                _logger.LogInformation($"Updated conversation {conversationId} title to '{title}'");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating conversation {conversationId} title");
                return false;
            }
        }
    }
}
