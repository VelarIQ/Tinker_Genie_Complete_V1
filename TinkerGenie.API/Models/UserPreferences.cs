namespace TinkerGenie.API.Models
{
    public class UserPreferences
    {
        public string UserId { get; set; } = "";
        public string PromptTime { get; set; } = "09:00";
        public string Timezone { get; set; } = "America/New_York";
        public string CommunicationStyle { get; set; } = "balanced";
        public string ResponseLength { get; set; } = "medium";
        public bool NudgeEnabled { get; set; } = true;
        public bool EmailSummary { get; set; } = false;
    }
}
