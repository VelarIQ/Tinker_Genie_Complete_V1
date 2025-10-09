using System.Threading.Tasks;

namespace TinkerGenie.API.Core.Interfaces
{
    public interface IOpenAIService
    {
        Task<string> GenerateResponseAsync(string prompt, string context);
    }
}
