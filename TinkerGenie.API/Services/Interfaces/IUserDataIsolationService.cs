using System.Security.Claims;
using System.Threading.Tasks;

namespace TinkerGenie.API.Services.Interfaces
{
    public interface IUserDataIsolationService
    {
        string GetUserId(ClaimsPrincipal? user);
        Task EnsureUserCollectionExistsAsync(string userId);
        string GetUserCollectionName(string userId);
        Task<IEnumerable<string>> GetUserCollectionsAsync();
        Task<bool> DeleteUserDataAsync(string userId);
    }
}
