using TinkerGenie.API.Core.Entities;

namespace TinkerGenie.API.Core.Interfaces
{
    /// <summary>
    /// Repository interface for User operations - NO dependencies on other services
    /// </summary>
    public interface IUserRepository
    {
        Task<User?> GetByEmailAsync(string email);
        Task<User?> GetByIdAsync(Guid id);
        Task<User> CreateAsync(User user);
        Task<User> UpdateAsync(User user);
        Task<bool> ExistsAsync(string email);
    }
}
