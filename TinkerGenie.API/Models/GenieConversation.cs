using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TinkerGenie.API.Models
{
    [Table("genie_conversations")]
    public class GenieConversation
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        
        public Guid UserId { get; set; }
        public Guid? GenieInstanceId { get; set; }
        public string? Title { get; set; }
        public string ConversationType { get; set; } = "chat";
        public string Status { get; set; } = "active";
        public int MessageCount { get; set; } = 0;
        public int? SatisfactionRating { get; set; }
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;
        public DateTime? EndedAt { get; set; }
        
        [Column(TypeName = "jsonb")]
        public string? KeyInsights { get; set; }
        
        [Column(TypeName = "jsonb")]
        public string? ActionItems { get; set; }
        
        [Column(TypeName = "jsonb")]
        public string? EmotionalContext { get; set; }
        
        public string[]? ContextTags { get; set; }
    }
}
