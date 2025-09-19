using Microsoft.EntityFrameworkCore;
using TinkerGenie.API.Models;

namespace TinkerGenie.API.Data
{
    public class TinkerGenieContext : DbContext
    {
        public TinkerGenieContext(DbContextOptions<TinkerGenieContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<UserProfile> UserProfiles { get; set; }
        public DbSet<GenieConversation> GenieConversations { get; set; }
        public DbSet<ConversationMessage> ConversationMessages { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure User entity
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Email).IsUnique();
                entity.Property(e => e.Email).IsRequired();
            });

            // Configure GenieConversation entity
            modelBuilder.Entity<GenieConversation>(entity =>
            {
                entity.HasKey(e => e.Id);
            });

            // Configure ConversationMessage entity
            modelBuilder.Entity<ConversationMessage>(entity =>
            {
                entity.HasKey(e => e.Id);
            });

            // Configure UserProfile entity
            modelBuilder.Entity<UserProfile>(entity =>
            {
                entity.HasKey(e => e.Id);
            });
        }
    }
}
