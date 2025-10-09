using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TinkerGenie.API.Data;

namespace TinkerGenie.API.Models
{
    /// <summary>
    /// September 2025 Tenant-Aware Curriculum Module
    /// Represents learning modules that can be customized per tenant
    /// </summary>
    [Table("curriculum_modules")]
    public class CurriculumModule : ITenantEntity
    {
        [Key]
        public Guid? Id { get; set; }

        [Required]
        public Guid TenantId { get; set; }

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = "";

        [MaxLength(1000)]
        public string? Description { get; set; }

        /// <summary>
        /// Module content in JSON format
        /// </summary>
        [Column(TypeName = "jsonb")]
        public string? Content { get; set; }

        /// <summary>
        /// Module type (LEADERSHIP, BUSINESS, TECHNICAL, etc.)
        /// </summary>
        [MaxLength(50)]
        public string ModuleType { get; set; } = "LEADERSHIP";

        /// <summary>
        /// Difficulty level (BEGINNER, INTERMEDIATE, ADVANCED)
        /// </summary>
        [MaxLength(20)]
        public string DifficultyLevel { get; set; } = "BEGINNER";

        /// <summary>
        /// Estimated completion time in minutes
        /// </summary>
        public int EstimatedMinutes { get; set; }

        /// <summary>
        /// Order/sequence number within the curriculum
        /// </summary>
        public int OrderIndex { get; set; }

        /// <summary>
        /// Whether this module is active and available
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Whether this module is required or optional
        /// </summary>
        public bool IsRequired { get; set; } = false;

        /// <summary>
        /// Prerequisites for this module
        /// </summary>
        public Guid[]? Prerequisites { get; set; }

        /// <summary>
        /// Tags for categorization and search
        /// </summary>
        public string[]? Tags { get; set; }

        /// <summary>
        /// Module metadata in JSON format
        /// </summary>
        [Column(TypeName = "jsonb")]
        public string? Metadata { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation property
        public virtual Tenant? Tenant { get; set; }
    }

    /// <summary>
    /// Module types for categorization
    /// </summary>
    public static class ModuleTypes
    {
        public const string LEADERSHIP = "LEADERSHIP";
        public const string BUSINESS = "BUSINESS";
        public const string TECHNICAL = "TECHNICAL";
        public const string COMMUNICATION = "COMMUNICATION";
        public const string STRATEGY = "STRATEGY";
        public const string OPERATIONS = "OPERATIONS";
        public const string FINANCE = "FINANCE";
        public const string MARKETING = "MARKETING";
        public const string SALES = "SALES";
        public const string CUSTOMER_SERVICE = "CUSTOMER_SERVICE";
    }

    /// <summary>
    /// Difficulty levels
    /// </summary>
    public static class DifficultyLevels
    {
        public const string BEGINNER = "BEGINNER";
        public const string INTERMEDIATE = "INTERMEDIATE";
        public const string ADVANCED = "ADVANCED";
        public const string EXPERT = "EXPERT";
    }
}



