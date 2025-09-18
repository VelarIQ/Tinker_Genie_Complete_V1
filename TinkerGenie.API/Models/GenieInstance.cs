using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TinkerGenie.API.Models
{
    public class GenieInstance
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        
        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;
        
        [Required]
        [MaxLength(50)]
        public string Type { get; set; } = string.Empty;
        
        public int HierarchyLevel { get; set; }
        public Guid? ParentGenieId { get; set; }
        public Guid? OwnerUserId { get; set; }
        
        [Column(TypeName = "jsonb")]
        public string PersonaConfig { get; set; } = "{}";
        
        public Guid[]? CurriculumAssignments { get; set; }
        
        [Column(TypeName = "jsonb")]
        public string? Permissions { get; set; }
        
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
