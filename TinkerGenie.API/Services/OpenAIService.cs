using OpenAI.Chat;

namespace TinkerGenie.API.Services;

public class OpenAIService : IOpenAIService
{
    private readonly ChatClient _chatClient;
    private readonly ILogger<OpenAIService> _logger;

    public OpenAIService(ChatClient chatClient, ILogger<OpenAIService> logger)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _logger = logger;
    }

    public async Task<string> GenerateResponseAsync(string systemPrompt, string userMessage)
    {
        try
        {
            // Create message list with explicit type
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(systemPrompt),
                new UserChatMessage(userMessage)
            };

            // Use correct options for OpenAI 2.1.0
            var options = new ChatCompletionOptions
            {
                MaxOutputTokenCount = 500,
                Temperature = 0.7f
            };

            var response = await _chatClient.CompleteChatAsync(messages, options);
            return response.Value.Content[0].Text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating OpenAI response for message: {Message}", userMessage);
            return "I apologize, but I'm having trouble processing your request right now. Please try again.";
        }
    }

    public Task<List<float>> GenerateEmbeddingAsync(string text)
    {
        try
        {
            // This would use OpenAI's embedding API
            // For now, return empty list as placeholder
            _logger.LogWarning("Embedding generation not yet implemented");
            return Task.FromResult(new List<float>());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating embedding for text: {Text}", text);
            return Task.FromResult(new List<float>());
        }
    }
}
