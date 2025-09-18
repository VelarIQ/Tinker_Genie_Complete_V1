namespace TinkerGenie.API.Services;

public interface IOpenAIService
{
    Task<string> GenerateResponseAsync(string systemPrompt, string userMessage);
    Task<List<float>> GenerateEmbeddingAsync(string text);
}
