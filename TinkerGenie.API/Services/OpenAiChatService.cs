using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using System.ClientModel;

namespace TinkerGenie.API.Services;

public sealed class OpenAiChatService : IChatService
{
    private readonly ChatClient _chat;
    private readonly ILogger<OpenAiChatService> _logger;

    public OpenAiChatService(ChatClient chat, ILogger<OpenAiChatService> logger)
    {
        _chat = chat;
        _logger = logger;
    }

    public async Task<string> GetReplyAsync(string userMessage, string? systemPrompt = null)
    {
        // ---- Explicit typing avoids CS0826 ("No best type found for implicitly-typed array") ----
        ChatMessage[] messages = new ChatMessage[]
        {
            new SystemChatMessage(systemPrompt ?? "You are Chris Cooper, a leadership mentor. Provide direct, actionable insights in 2-3 sentences max. Focus on results and action."),
            new UserChatMessage(userMessage)
        };

        try
        {
            // v2 API
            var result = await _chat.CompleteChatAsync(messages);
            // Concatenate content parts safely
            var text = string.Concat(result.Value.Content.Select(p => p.Text));
            return string.IsNullOrWhiteSpace(text)
                ? "(No content returned from model.)"
                : text;
        }
        // System.ClientModel v1.6.x -> ClientResultException has Status & Message (no .Response)
        catch (ClientResultException e) when (e.Status == 429)
        {
            _logger.LogWarning("OpenAI 429: {Message}", e.Message);
            // Graceful degraded message (so UX doesn't implode while quota is out)
            return "⚠️ The AI provider is rate-limited or out of quota. Please try again shortly.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI chat failed");
            return "❌ Sorry—AI service failed processing this request.";
        }
    }
}
