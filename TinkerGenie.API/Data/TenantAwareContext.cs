using Microsoft.EntityFrameworkCore;
using System.Text;
using TinkerGenie.API.Models;
using TinkerGenie.API.Services;

namespace TinkerGenie.API.Data
{
    /// <summary>
    /// September 2025 Multi-Tenant Entity Framework Context
    /// Implements automatic tenant isolation through global query filters
    /// Uses TenantResolver to avoid circular dependencies
    /// Optimized for 100+ concurrent users
    /// </summary>
    public class TenantAwareContext : DbContext
    {
        private readonly ITenantResolver _tenantResolver;
        private readonly ILogger<TenantAwareContext> _logger;

        public TenantAwareContext(
            DbContextOptions<TenantAwareContext> options,
            ITenantResolver tenantResolver,
            ILogger<TenantAwareContext> logger) : base(options)
        {
            _tenantResolver = tenantResolver;
            _logger = logger;
        }

        // Core entities with tenant isolation
        public DbSet<Tenant> Tenants { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<GenieInstance> GenieInstances { get; set; }
        public DbSet<GenieConversation> GenieConversations { get; set; }
        public DbSet<ConversationMessage> ConversationMessages { get; set; }
        public DbSet<TenantAuditLog> TenantAuditLogs { get; set; }
        public DbSet<TenantResourceUsage> TenantResourceUsage { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure tenant entity
            ConfigureTenant(modelBuilder);
            
            // Configure entities with tenant isolation
            ConfigureUser(modelBuilder);
            ConfigureGenieInstance(modelBuilder);
            ConfigureGenieConversation(modelBuilder);
            ConfigureConversationMessage(modelBuilder);
            ConfigureTenantAuditLog(modelBuilder);
            ConfigureTenantResourceUsage(modelBuilder);

            // Apply naming conventions (snake_case)
            ApplyNamingConventions(modelBuilder);
        }

        private void ConfigureTenant(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Tenant>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Domain).HasMaxLength(100);
                entity.Property(e => e.IsActive).HasDefaultValue(true);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasIndex(e => e.Domain).IsUnique();
                entity.HasIndex(e => e.Name);
            });
        }

        private void ConfigureUser(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Email).IsRequired().HasMaxLength(255);
                entity.Property(e => e.FirstName).HasMaxLength(100);
                entity.Property(e => e.LastName).HasMaxLength(100);
                entity.HasIndex(e => e.Email);
                entity.HasIndex(e => e.TenantId);
                
                // Global query filter for tenant isolation
                entity.HasQueryFilter(e => e.TenantId == GetCurrentTenantId());
            });
        }

        private void ConfigureGenieInstance(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<GenieInstance>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
                entity.HasIndex(e => e.TenantId);
                
                entity.HasQueryFilter(e => e.TenantId == GetCurrentTenantId());
            });
        }

        private void ConfigureGenieConversation(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<GenieConversation>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Title).HasMaxLength(500);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasIndex(e => e.TenantId);
                
                entity.HasQueryFilter(e => e.TenantId == GetCurrentTenantId());
            });
        }

        private void ConfigureConversationMessage(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ConversationMessage>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Content).IsRequired();
                entity.Property(e => e.Role).IsRequired().HasMaxLength(50);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasIndex(e => e.TenantId);
                entity.HasIndex(e => e.ConversationId);
                
                entity.HasQueryFilter(e => e.TenantId == GetCurrentTenantId());
            });
        }

        private void ConfigureTenantAuditLog(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TenantAuditLog>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Action).IsRequired().HasMaxLength(100);
                entity.Property(e => e.EntityType).HasMaxLength(100);
                entity.Property(e => e.EntityId).HasMaxLength(100);
                entity.Property(e => e.UserId).HasMaxLength(100);
                entity.Property(e => e.Timestamp).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasIndex(e => e.TenantId);
                entity.HasIndex(e => e.Timestamp);
                entity.HasIndex(e => e.UserId);
                
                // Global query filter for tenant isolation
                entity.HasQueryFilter(e => e.TenantId == GetCurrentTenantId());
            });
        }

        private void ConfigureTenantResourceUsage(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TenantResourceUsage>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ResourceType).IsRequired().HasMaxLength(100);
                entity.Property(e => e.UsageCount).HasDefaultValue(0);
                entity.Property(e => e.LastUpdated).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasIndex(e => e.TenantId);
                entity.HasIndex(e => e.ResourceType);
                entity.HasIndex(e => e.LastUpdated);
                
                // Global query filter for tenant isolation
                entity.HasQueryFilter(e => e.TenantId == GetCurrentTenantId());
            });
        }

        private void ApplyNamingConventions(ModelBuilder modelBuilder)
        {
            // Convert all table and column names to snake_case
            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                var tableName = entity.GetTableName();
                if (!string.IsNullOrEmpty(tableName))
                {
                    entity.SetTableName(ToSnakeCase(tableName));
                }

                foreach (var property in entity.GetProperties())
                {
                    var columnName = property.Name;
                    if (!string.IsNullOrEmpty(columnName))
                    {
                        property.SetColumnName(ToSnakeCase(columnName));
                    }
                }
            }
        }

        private Guid GetCurrentTenantId()
        {
            try
            {
                return _tenantResolver?.GetCurrentTenantId() ?? Guid.Parse("58073446-62bd-48ae-840a-6a0f70c21d5f");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error getting current tenant ID, using default");
                return Guid.Parse("58073446-62bd-48ae-840a-6a0f70c21d5f");
            }
        }

        private static string ToSnakeCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var result = new StringBuilder();
            for (int i = 0; i < input.Length; i++)
            {
                if (char.IsUpper(input[i]) && i > 0)
                {
                    result.Append('_');
                }
                result.Append(char.ToLower(input[i]));
            }
            return result.ToString();
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SetTenantIdForNewEntities();
            await AddAuditLogsAsync();
            return await base.SaveChangesAsync(cancellationToken);
        }

        private void SetTenantIdForNewEntities()
        {
            var currentTenantId = GetCurrentTenantId();
            
            foreach (var entry in ChangeTracker.Entries<ITenantEntity>())
            {
                if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
                {
                    entry.Entity.TenantId = currentTenantId;
                }
            }
        }

        private async Task AddAuditLogsAsync()
        {
            var currentTenantId = GetCurrentTenantId();
            var auditEntries = new List<TenantAuditLog>();

            foreach (var entry in ChangeTracker.Entries<ITenantEntity>())
            {
                if (entry.State == EntityState.Added || entry.State == EntityState.Modified || entry.State == EntityState.Deleted)
                {
                    var auditLog = new TenantAuditLog
                    {
                        Id = Guid.NewGuid(),
                        TenantId = currentTenantId,
                        Action = entry.State.ToString(),
                        EntityType = entry.Entity.GetType().Name,
                        EntityId = entry.Entity.Id?.ToString(),
                        Timestamp = DateTime.UtcNow,
                        Changes = System.Text.Json.JsonSerializer.Serialize(entry.CurrentValues.ToObject())
                    };
                    
                    auditEntries.Add(auditLog);
                }
            }

            if (auditEntries.Any())
            {
                await TenantAuditLogs.AddRangeAsync(auditEntries);
            }
        }
    }

    /// <summary>
    /// Interface for entities that belong to a tenant
    /// </summary>
    public interface ITenantEntity
    {
        Guid TenantId { get; set; }
        Guid? Id { get; set; }
    }
}