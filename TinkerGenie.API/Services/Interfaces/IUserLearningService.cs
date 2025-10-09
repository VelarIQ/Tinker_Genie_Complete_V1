using System.Threading.Tasks;

namespace TinkerGenie.API.Services.Interfaces
{
    public interface IUserLearningService
    {
        Task<UserLearningProgress> GetUserLearningProgress(string userId);
        Task UpdateUserLearningProgress(string userId, UserLearningProgress progress);
    }

    public class UserLearningProgress
    {
        public int CurrentDay { get; set; }
        public double SkillLevel { get; set; }
        public List<string> CompletedModules { get; set; } = new List<string>();
    }
}
