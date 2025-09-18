using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TinkerGenie.API.Models
{
    [Table("conversation_messages")]
    public class ConversationMessage
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        
        public Guid ConversationId { get; set; }
        public Guid? UserId { get; set; }
        
        [Required]
        public string Sender { get; set; } = ""; // 'user' or 'genie'
        
        [Required]
        public string MessageText { get; set; } = "";
        
        public string? Content { get; set; }
        public string MessageType { get; set; } = "text";
        public string? AiModelUsed { get; set; }
        
        [Column(TypeName = "jsonb")]
        public string? GenerationContext { get; set; }
        
        public string? UserReaction { get; set; }
        public int? ProcessingTimeMs { get; set; }
        public bool IsUser { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
