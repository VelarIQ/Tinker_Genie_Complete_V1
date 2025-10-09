using System.ComponentModel.DataAnnotations;
using TinkerGenie.API.Data;

namespace TinkerGenie.API.Models
{
    /// <summary>
    /// September 2025 Multi-Tenant Resource Usage Tracking
    /// Monitors resource consumption per tenant for billing and limits
    /// </summary>
    public class TenantResourceUsage : ITenantEntity
    {
        [Key]
        public Guid? Id { get; set; }
        
        [Required]
        public Guid TenantId { get; set; }
        
        [Required]
        [MaxLength(100)]
        public string ResourceType { get; set; } = "";
        
        public long UsageCount { get; set; } = 0;
        
        public decimal UsageCost { get; set; } = 0;
        
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
        
        public DateTime PeriodStart { get; set; }
        
        public DateTime PeriodEnd { get; set; }
        
        public string? Metadata { get; set; }
    }
}