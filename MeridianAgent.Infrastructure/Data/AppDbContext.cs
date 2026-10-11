namespace MeridianAgent.Infrastructure.Data;

using MeridianAgent.Domain;
using MeridianAgent.Domain.Entities;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// SQLite store for structured traveler state — one row per profile, plus the day-wise chat
/// history the traveler can browse and bookmark. Vector/RAG data still lives in Qdrant and
/// destination rulebooks stay on disk; this context owns the profile and the transcript.
/// </summary>
public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<UserProfile> Profiles => Set<UserProfile>();

    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();

    public DbSet<ChatMessageEntity> ChatMessages => Set<ChatMessageEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserProfile>(entity =>
        {
            entity.ToTable("Profiles");
            entity.HasKey(profile => profile.Id);

            entity.Property(profile => profile.Id).HasMaxLength(64).ValueGeneratedNever();

            entity.Property(profile => profile.DisplayName).HasMaxLength(120);
            entity.Property(profile => profile.Destination).HasMaxLength(200);
            entity.Property(profile => profile.AttireNote).HasMaxLength(500);
            entity.Property(profile => profile.HomeBase).HasMaxLength(200);

            // Onboarding preferences captured as the traveler picked them.
            entity.Property(profile => profile.TravelStyle).HasMaxLength(64);
            entity.Property(profile => profile.EnergyPattern).HasMaxLength(64);
            entity.Property(profile => profile.BudgetComfort).HasMaxLength(64);
            entity.Property(profile => profile.Mobility).HasMaxLength(64);
            entity.Property(profile => profile.TravelGroup).HasMaxLength(64);

            // Lists: JSON arrays plus the enums the agents read.
            entity.Property(profile => profile.DietaryRules).HasConversion(Conversions.StringList, Conversions.StringListComparer).HasColumnName("dietary_rules_json");
            entity.Property(profile => profile.Interests).HasConversion(Conversions.StringList, Conversions.StringListComparer).HasColumnName("interests_json");
            entity.Property(profile => profile.AvoidList).HasConversion(Conversions.StringList, Conversions.StringListComparer).HasColumnName("avoid_list_json");
            entity.Property(profile => profile.DietaryRestrictions).HasConversion(Conversions.StringList, Conversions.StringListComparer).HasColumnName("dietary_restrictions_json");
            entity.Property(profile => profile.FavoriteCuisines).HasConversion(Conversions.StringList, Conversions.StringListComparer).HasColumnName("favorite_cuisines_json");
            entity.Property(profile => profile.DiningVibe).HasConversion(Conversions.StringList, Conversions.StringListComparer).HasColumnName("dining_vibe_json");
            entity.Property(profile => profile.CoreInterests).HasConversion(Conversions.StringList, Conversions.StringListComparer).HasColumnName("core_interests_json");
            entity.Property(profile => profile.HardAvoids).HasConversion(Conversions.StringList, Conversions.StringListComparer).HasColumnName("hard_avoids_json");

            entity.Property(profile => profile.TravelerProfile).HasConversion(Conversions.EnumByName<TravelerProfile>()).HasMaxLength(32);
            entity.Property(profile => profile.GroupComposition).HasConversion(Conversions.EnumByName<GroupComposition>()).HasMaxLength(32);
            entity.Property(profile => profile.Pacing).HasConversion(Conversions.EnumByName<PacingPreference>()).HasMaxLength(32);
            entity.Property(profile => profile.Budget).HasConversion(Conversions.EnumByName<BudgetRange>()).HasMaxLength(32);
        });

        modelBuilder.Entity<ChatSession>(entity =>
        {
            entity.ToTable("ChatSessions");
            entity.HasKey(session => session.Id);

            // The profile is reachable without a navigation property, so the reference is still
            // enforced and a stray session cannot outlive the traveler it belongs to.
            entity.HasOne<UserProfile>()
                .WithMany()
                .HasForeignKey(session => session.UserProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(session => session.UserProfileId).HasMaxLength(64).IsRequired();

            // One session per traveler per calendar day. This is also the guard against two
            // concurrent first-messages-on-a-new-day both creating a row for that day.
            entity.HasIndex(session => new { session.UserProfileId, session.Date }).IsUnique();
        });

        modelBuilder.Entity<ChatMessageEntity>(entity =>
        {
            entity.ToTable("ChatMessages");
            entity.HasKey(message => message.Id);

            entity.HasOne(message => message.Session)
                .WithMany(session => session.Messages)
                .HasForeignKey(message => message.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(message => message.Role).HasMaxLength(16).IsRequired();
            entity.Property(message => message.Content).IsRequired();

            entity.HasIndex(message => message.SessionId);
        });
    }
}
