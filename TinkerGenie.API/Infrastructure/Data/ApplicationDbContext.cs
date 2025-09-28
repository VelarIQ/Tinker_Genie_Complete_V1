using Microsoft.EntityFrameworkCore;
using TinkerGenie.API.Core.Entities;

namespace TinkerGenie.API.Infrastructure.Data
{
    /// <summary>
    /// Clean DbContext - depends only on entities, no circular dependencies
    /// </summary>
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            
            // Additional configuration if needed - User entity uses attributes
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasIndex(e => e.Email).IsUnique();
            });
        }
    }
}
