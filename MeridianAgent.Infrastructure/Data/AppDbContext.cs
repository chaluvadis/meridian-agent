namespace MeridianAgent.Infrastructure.Data;

using MeridianAgent.Domain;
using MeridianAgent.Domain.Entities;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// SQLite store for structured traveler state — one row per profile. Vector/RAG data still lives in
/// Qdrant and destination rulebooks stay on disk; this context only owns the profile.
/// </summary>
public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<UserProfile> Profiles => Set<UserProfile>();

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
    }
}
