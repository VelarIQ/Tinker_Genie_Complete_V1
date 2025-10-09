using System.ComponentModel.DataAnnotations;
using TinkerGenie.API.Data;

namespace TinkerGenie.API.Models
{
    /// <summary>
    /// September 2025 Multi-Tenant Audit Log
    /// Tracks all tenant-specific operations for compliance and security
    /// </summary>
    public class TenantAuditLog : ITenantEntity
    {
        [Key]
        public Guid? Id { get; set; }
        
        [Required]
        public Guid TenantId { get; set; }
        
        [Required]
        [MaxLength(100)]
        public string Action { get; set; } = "";
        
        [MaxLength(100)]
        public string? EntityType { get; set; }
        
        [MaxLength(100)]
        public string? EntityId { get; set; }
        
        [MaxLength(100)]
        public string? UserId { get; set; }
        
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        
        public string? Changes { get; set; }
        
        public string? IpAddress { get; set; }
        
        public string? UserAgent { get; set; }
    }
}