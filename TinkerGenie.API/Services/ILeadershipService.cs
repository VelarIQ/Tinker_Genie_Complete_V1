using TinkerGenie.API.Models;

namespace TinkerGenie.API.Services
{
    public interface ILeadershipService
    {
        Task<DailyPrompt?> GetDailyPromptAsync(string userId);
        Task<bool> CompletePromptAsync(string userId, int dayNumber, Dictionary<string, string> responses, string? reflectionNotes = null);
        Task<List<string>> GenerateFollowUpQuestionsAsync(int dayNumber, Dictionary<string, string> responses);
        Task<UserProgress> GetUserProgressAsync(string userId);
        Task<bool> UpdateUserPreferencesAsync(string userId, UserPreferences preferences);
        Task<List<DailyPrompt>> GetMissedPromptsAsync(string userId);
        Task<bool> SendNudgeAsync(string userId, string nudgeType);
    }

    public class DailyPrompt
    {
        public int DayNumber { get; set; }
        public string Title { get; set; } = string.Empty;
        public string PromptText { get; set; } = string.Empty;
        public List<string> FillInBlanks { get; set; } = new();
        public string TaskInstructions { get; set; } = string.Empty;
        public int EstimatedTimeMinutes { get; set; }
    }

    public class UserProgress
    {
        public int CurrentDay { get; set; }
        public int CompletedDays { get; set; }
        public int CurrentStreak { get; set; }
        public DateTime LastCompleted { get; set; }
        public List<int> MissedDays { get; set; } = new();
    }

    public class UserPreferences
    {
        public string CommunicationStyle { get; set; } = "medium";
        public TimeSpan PromptDeliveryTime { get; set; } = new TimeSpan(9, 0, 0);
        public string Timezone { get; set; } = "America/New_York";
        public bool JourneyPaused { get; set; }
    }
}
