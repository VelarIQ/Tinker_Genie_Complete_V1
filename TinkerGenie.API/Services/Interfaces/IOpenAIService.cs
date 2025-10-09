using System.Collections.Generic;
using System.Threading.Tasks;

namespace TinkerGenie.API.Services.Interfaces
{
    public interface IOpenAIService
    {
        Task<string> GenerateResponseAsync(string systemInstruction, string userMessage, CancellationToken cancellationToken = default);
        Task<List<float>> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);
    }
}


