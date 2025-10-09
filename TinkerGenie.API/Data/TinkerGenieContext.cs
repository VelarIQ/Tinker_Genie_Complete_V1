using Microsoft.EntityFrameworkCore;
using TinkerGenie.API.Models;

namespace TinkerGenie.API.Data
{
    public class TinkerGenieContext : DbContext
    {
        public TinkerGenieContext(DbContextOptions<TinkerGenieContext> options)
            : base(options)
        {
        }

        // Existing DbSets
        public DbSet<SimpleUser> Users { get; set; }
        public DbSet<GenieConversation> GenieConversations { get; set; }
        public DbSet<ConversationMessage> ConversationMessages { get; set; }

        // New DbSets for Learning Tracking
        public DbSet<UserSkillProgression> UserSkillProgressions { get; set; }
        public DbSet<UserLearningGoal> UserLearningGoals { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure UserSkillProgression
            modelBuilder.Entity<UserSkillProgression>(entity =>
            {
                entity.ToTable("user_skill_progression");
                
                entity.HasKey(e => e.Id);
                
                entity.Property(e => e.Id)
                    .HasColumnName("id")
                    .ValueGeneratedOnAdd();
                
                entity.Property(e => e.UserId)
                    .HasColumnName("user_id")
                    .IsRequired();
                
                entity.Property(e => e.SkillDomain)
                    .HasColumnName("skill_domain")
                    .HasConversion<string>()
                    .IsRequired();
                
                entity.Property(e => e.SkillLevel)
                    .HasColumnName("skill_level")
                    .HasColumnType("double precision")
                    .IsRequired();
                
                entity.Property(e => e.MeasuredAt)
                    .HasColumnName("measured_at")
                    .HasDefaultValueSql("CURRENT_TIMESTAMP");
                
                entity.HasIndex(e => new { e.UserId, e.SkillDomain });
            });

            // Configure UserLearningGoal
            modelBuilder.Entity<UserLearningGoal>(entity =>
            {
                entity.ToTable("user_learning_goals");
                
                entity.HasKey(e => e.Id);
                
                entity.Property(e => e.Id)
                    .HasColumnName("id")
                    .ValueGeneratedOnAdd();
                
                entity.Property(e => e.UserId)
                    .HasColumnName("user_id")
                    .IsRequired();
                
                entity.Property(e => e.LearningGoal)
                    .HasColumnName("learning_goal")
                    .HasMaxLength(500)
                    .IsRequired();
                
                entity.Property(e => e.ProgressNotes)
                    .HasColumnName("progress_notes")
                    .HasMaxLength(1000);
                
                entity.Property(e => e.CreatedAt)
                    .HasColumnName("created_at")
                    .HasDefaultValueSql("CURRENT_TIMESTAMP");
                
                entity.Property(e => e.TargetCompletionDate)
                    .HasColumnName("target_completion_date");
                
                entity.HasIndex(e => e.UserId);
            });
        }
    }

    // New model classes
    public class UserSkillProgression
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public SkillDomain SkillDomain { get; set; }
        public double SkillLevel { get; set; }
        public DateTime MeasuredAt { get; set; }
    }

    public class UserLearningGoal
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string LearningGoal { get; set; } = string.Empty;
        public string ProgressNotes { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? TargetCompletionDate { get; set; }
    }

    // Enum to match the service implementation
    public enum SkillDomain 
    {
        BusinessStrategy,
        EmotionalIntelligence,
        Leadership,
        PersonalGrowth,
        CommunicationSkills
    }
}
