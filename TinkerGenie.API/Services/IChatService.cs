using System.Threading.Tasks;

namespace TinkerGenie.API.Services;

public interface IChatService
{
    Task<string> GetReplyAsync(string userMessage, string? systemPrompt = null);
}
