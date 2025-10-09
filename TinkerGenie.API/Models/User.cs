using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TinkerGenie.API.Data;

namespace TinkerGenie.API.Models
{
    [Table("users")]
    public class User
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        
        [Required]
        [EmailAddress]
        public string Email { get; set; } = "";
        
        public string? Username { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PasswordHash { get; set; }
        public bool RequiresPasswordSetup { get; set; } = false;
        public bool RequiresPasswordChange { get; set; } = false;
        public DateTime? LastPasswordChange { get; set; }
        public string? TbbUserId { get; set; }
        public Guid? TenantId { get; set; }
        public string Role { get; set; } = "user";
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
