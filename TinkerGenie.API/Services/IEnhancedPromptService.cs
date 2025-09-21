namespace TinkerGenie.API.Services
{
    public interface IEnhancedPromptService
    {
        Task<BurningFireResponse> HandleBurningFire(string userId, string issue);
        Task<TinkerLevelResponse> HandleTinkerLevel(string userId, string challenge);
    }

    public class BurningFireResponse
    {
        public string IssueIdentified { get; set; } = "";
        public List<Solution> Solutions { get; set; } = new();
        public string QuickWin { get; set; } = "";
        public string FormattedResponse { get; set; } = "";
    }

    public class TinkerLevelResponse
    {
        public string StrategicFocus { get; set; } = "";
        public List<string> DataPoints { get; set; } = new();
        public List<string> ChallengeQuestions { get; set; } = new();
        public List<StrategicAction> StrategicPath { get; set; } = new();
        public string TenXQuestion { get; set; } = "";
        public string FormattedResponse { get; set; } = "";
    }

    public class Solution
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string ModuleLink { get; set; } = "";
        public string ModuleName { get; set; } = "";
    }

    public class StrategicAction
    {
        public int Step { get; set; }
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string ModuleLink { get; set; } = "";
        public string Note { get; set; } = "";
    }
}
