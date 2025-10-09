using System.Collections.Generic;
using System.Threading.Tasks;
using TinkerGenie.API.Services.Models;

namespace TinkerGenie.API.Services.Interfaces
{
    public interface IEnhancedPromptService
    {
        Task<BurningFireResponse> HandleBurningFire(string userId, string issue);
        Task<TinkerLevelResponse> HandleTinkerLevel(string userId, string challenge);
        Task<List<Solution>> SearchAndGenerateSolutions(string issue, int limit = 3);
        Task<TinkerLevelSearchResult> SearchForStrategicInsights(string topic, string userId, int currentDay);
    }
}
