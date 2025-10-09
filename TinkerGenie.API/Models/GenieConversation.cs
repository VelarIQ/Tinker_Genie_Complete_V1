using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TinkerGenie.API.Data;

namespace TinkerGenie.API.Models
{
    [Table("genie_conversations")]
    public class GenieConversation : ITenantEntity
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        
        public Guid TenantId { get; set; }
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
        
        /// <summary>
        /// Additional metadata in JSON format
        /// </summary>
        [Column(TypeName = "jsonb")]
        public string? Metadata { get; set; }
        
        /// <summary>
        /// When the conversation was created
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// When the conversation was last updated
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        
        // Navigation properties
        public virtual User? User { get; set; }
        public virtual GenieInstance? GenieInstance { get; set; }

        Guid? ITenantEntity.Id
        {
            get => Id;
            set
            {
                if (value.HasValue)
                {
                    Id = value.Value;
                }
            }
        }
    }
}
