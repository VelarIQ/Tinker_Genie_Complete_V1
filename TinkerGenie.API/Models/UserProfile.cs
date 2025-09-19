using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TinkerGenie.API.Models
{
    [Table("user_profiles")]
    public class UserProfile
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        
        public Guid UserId { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? BusinessName { get; set; }
        public int CurrentDay { get; set; } = 1;
        public string CommunicationStyle { get; set; } = "balanced";
        public string PreferredResponseLength { get; set; } = "medium";
        public bool JourneyPaused { get; set; } = false;
        public string Timezone { get; set; } = "America/New_York";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
