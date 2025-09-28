using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TinkerGenie.API.Core.Entities
{
    /// <summary>
    /// BULLETPROOF User entity - matches the nuclear rebuilt database table
    /// </summary>
    [Table("users")]
    public class User
    {
        [Key]
        [Column("id")]
        public Guid Id { get; set; } = Guid.NewGuid();
        
        [Required]
        [EmailAddress]
        [Column("email")]
        public string Email { get; set; } = "";
        
        [Column("username")]
        public string? Username { get; set; }
        
        [Column("password_hash")]
        public string? PasswordHash { get; set; }
        
        // Profile Information
        [Column("first_name")]
        public string? FirstName { get; set; }
        
        [Column("last_name")]
        public string? LastName { get; set; }
        
        [Column("display_name")]
        public string? DisplayName { get; set; }
        
        // Security & Access Control
        [Column("role")]
        public string Role { get; set; } = "user";
        
        [Column("is_active")]
        public bool IsActive { get; set; } = true;
        
        [Column("is_verified")]
        public bool IsVerified { get; set; } = false;
        
        [Column("requires_password_setup")]
        public bool RequiresPasswordSetup { get; set; } = false;
        
        [Column("requires_password_change")]
        public bool RequiresPasswordChange { get; set; } = false;
        
        [Column("failed_login_attempts")]
        public int FailedLoginAttempts { get; set; } = 0;
        
        [Column("account_locked_until")]
        public DateTime? AccountLockedUntil { get; set; }
        
        [Column("last_password_change")]
        public DateTime? LastPasswordChange { get; set; }
        
        [Column("last_login")]
        public DateTime? LastLogin { get; set; }
        
        // Business Context
        [Column("business_name")]
        public string? BusinessName { get; set; }
        
        [Column("business_role")]
        public string? BusinessRole { get; set; }
        
        // System Integration
        [Column("external_id")]
        public string? ExternalId { get; set; }
        
        [Column("google_id")]
        public string? GoogleId { get; set; }
        
        [Column("microsoft_id")]
        public string? MicrosoftId { get; set; }
        
        [Column("apple_id")]
        public string? AppleId { get; set; }
        
        // Preferences & Settings (stored as JSON strings)
        [Column("preferences")]
        public string Preferences { get; set; } = "{}";
        
        [Column("metadata")]
        public string Metadata { get; set; } = "{}";
        
        // Audit Trail
        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        
        [Column("created_by")]
        public Guid? CreatedBy { get; set; }
        
        [Column("updated_by")]
        public Guid? UpdatedBy { get; set; }
        
        // Data Management
        [Column("migration_source")]
        public string? MigrationSource { get; set; }
        
        [Column("data_version")]
        public int DataVersion { get; set; } = 1;

        // Helper methods - no external dependencies
        public bool HasPassword => !string.IsNullOrEmpty(PasswordHash);
        public string GetDisplayName => DisplayName ?? FirstName ?? Username ?? Email.Split('@')[0];
        public bool IsLocked => AccountLockedUntil.HasValue && AccountLockedUntil > DateTime.UtcNow;
        public bool IsAdmin => Role == "admin";
        public bool IsTinkerMember => Role == "tinker_member" || Role == "admin";
    }
}