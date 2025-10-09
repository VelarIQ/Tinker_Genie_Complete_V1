using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using TinkerGenie.API.Services.Interfaces;

namespace TinkerGenie.API.Services
{
    public sealed class OpenAIService : IOpenAIService
    {
        private readonly ChatClient _chatClient;
        private readonly ILogger<OpenAIService> _logger;

        public OpenAIService(ChatClient chatClient, ILogger<OpenAIService> logger)
        {
            _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
            _logger = logger;
        }

        public async Task<string> GenerateResponseAsync(string systemInstruction, string userMessage, CancellationToken cancellationToken = default)
        {
            try
            {
                var messages = new List<ChatMessage>
                {
                    new SystemChatMessage(systemInstruction),
                    new UserChatMessage(userMessage)
                };

                var options = new ChatCompletionOptions
                {
                    MaxOutputTokenCount = 500,
                    Temperature = 0.7f
                };

                var response = await _chatClient.CompleteChatAsync(messages, options, cancellationToken);
                if (response.Value.Content.Count > 0)
                {
                    return response.Value.Content[0].Text;
                }

                _logger.LogWarning("OpenAI returned empty content for message");
                return "I'm still thinking about that. Give me another angle to work with.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating response from OpenAI");
                return "I'm having trouble connecting right now, but I'm here for you. Try again in a moment.";
            }
        }

        public async Task<List<float>> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogWarning("Embedding generation not yet implemented");
                return await Task.FromResult(new List<float>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating embedding");
                return new List<float>();
            }
        }
    }
}
