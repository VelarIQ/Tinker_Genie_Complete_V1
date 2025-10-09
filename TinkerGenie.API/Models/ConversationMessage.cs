using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TinkerGenie.API.Data;

namespace TinkerGenie.API.Models
{
    [Table("conversation_messages")]
    public class ConversationMessage : ITenantEntity
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        
        public Guid TenantId { get; set; }
        
        public Guid ConversationId { get; set; }
        public Guid? UserId { get; set; }
        
        [Required]
        public string Sender { get; set; } = ""; // 'user' or 'genie'
        
        [Required]
        public string MessageText { get; set; } = "";
        
        /// <summary>
        /// Message content - primary content field
        /// </summary>
        [Required]
        public string Content { get; set; } = "";
        
        public string MessageType { get; set; } = "text";
        public string? AiModelUsed { get; set; }
        
        [Column(TypeName = "jsonb")]
        public string? GenerationContext { get; set; }
        
        public string? UserReaction { get; set; }
        public int? ProcessingTimeMs { get; set; }
        public bool IsUser { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// Role of the message sender (user, assistant, system)
        /// </summary>
        [Required]
        public string Role { get; set; } = "user";
        
        /// <summary>
        /// Additional metadata in JSON format
        /// </summary>
        [Column(TypeName = "jsonb")]
        public string? Metadata { get; set; }
        
        // Navigation properties
        public virtual GenieConversation? Conversation { get; set; }

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
