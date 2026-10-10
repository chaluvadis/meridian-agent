namespace MeridianAgent.Domain.Entities;

/// <summary>One selectable option in the onboarding wizard.</summary>
/// <param name="Value">Stored in the profile (no emoji — data stays clean).</param>
/// <param name="Label">Rendered next to the control, emoji first for quick scanning.</param>
public sealed record OnboardingOption(string Value, string Label);

/// <summary>
/// The onboarding capture layer: every choice the traveler makes, kept as the plain strings they
/// picked. <see cref="BuildProfile" /> folds those choices into the agent-facing
/// <see cref="UserProfile" /> fields, so the Context, Search, Safeguard, Synthesizer and
/// Proactive agents keep reading exactly the same contract as before onboarding existed.
/// </summary>
public sealed class TravelerOnboardingDraft
{
    // ---- Single-choice catalogs --------------------------------------------------------

    public static IReadOnlyList<OnboardingOption> TravelStyles { get; } = new[]
    {
        new OnboardingOption("Slow Explorer", "🐢 Slow Explorer"),
        new OnboardingOption("Balanced", "⚖️ Balanced"),
        new OnboardingOption("Efficient", "⚡ Efficient"),
        new OnboardingOption("Cultural Immersion", "🎭 Cultural Immersion"),
    };

    public static IReadOnlyList<OnboardingOption> EnergyPatterns { get; } = new[]
    {
        new OnboardingOption("Morning Person", "🌅 Morning Person"),
        new OnboardingOption("Afternoon Person", "☀️ Afternoon Person"),
        new OnboardingOption("Night Owl", "🌙 Night Owl"),
        new OnboardingOption("Varies", "🌤️ Varies"),
    };

    public static IReadOnlyList<OnboardingOption> BudgetComforts { get; } = new[]
    {
        new OnboardingOption("Budget-Conscious", "🪙 Budget-Conscious"),
        new OnboardingOption("Mid-Range", "💳 Mid-Range"),
        new OnboardingOption("Splurge Occasionally", "✨ Splurge Occasionally"),
        new OnboardingOption("Money No Object", "💰 Money No Object"),
    };

    public static IReadOnlyList<OnboardingOption> MobilityOptions { get; } = new[]
    {
        new OnboardingOption("Comfortable Walking", "🚶 Comfortable Walking"),
        new OnboardingOption("Short Walks + Transit", "🚇 Short Walks + Transit"),
        new OnboardingOption("Minimize Walking", "🚕 Minimize Walking"),
    };

    public static IReadOnlyList<OnboardingOption> TravelGroups { get; } = new[]
    {
        new OnboardingOption("Solo", "🧍 Solo"),
        new OnboardingOption("Couple", "👫 Couple"),
        new OnboardingOption("Family with Kids", "👨‍👩‍👧 Family with Kids"),
        new OnboardingOption("Friends Group", "👯 Friends Group"),
    };

    // ---- Multi-choice catalogs ---------------------------------------------------------

    public static IReadOnlyList<OnboardingOption> DietaryOptions { get; } = new[]
    {
        new OnboardingOption("Vegetarian", "🥬 Vegetarian"),
        new OnboardingOption("Vegan", "🌱 Vegan"),
        new OnboardingOption("Halal", "🍖 Halal"),
        new OnboardingOption("Kosher", "✡️ Kosher"),
        new OnboardingOption("Gluten-Free", "🌾 Gluten-Free"),
        new OnboardingOption("Dairy-Free", "🥛 Dairy-Free"),
        new OnboardingOption("None", "➖ None"),
    };

    public static IReadOnlyList<OnboardingOption> Cuisines { get; } = new[]
    {
        new OnboardingOption("Italian", "🍝 Italian"),
        new OnboardingOption("Japanese", "🍣 Japanese"),
        new OnboardingOption("Middle Eastern", "🥙 Middle Eastern"),
        new OnboardingOption("Indian", "🍛 Indian"),
        new OnboardingOption("Mexican", "🌮 Mexican"),
        new OnboardingOption("French", "🥐 French"),
        new OnboardingOption("Local/Traditional", "🕌 Local/Traditional"),
        new OnboardingOption("Thai", "🍜 Thai"),
        new OnboardingOption("Mediterranean", "🫒 Mediterranean"),
    };

    public static IReadOnlyList<OnboardingOption> DiningVibes { get; } = new[]
    {
        new OnboardingOption("Quiet & Intimate", "🕯️ Quiet & Intimate"),
        new OnboardingOption("Lively & Social", "🎉 Lively & Social"),
        new OnboardingOption("Street Food", "🚚 Street Food"),
        new OnboardingOption("Fine Dining", "🍷 Fine Dining"),
        new OnboardingOption("Casual Cafés", "☕ Casual Cafés"),
    };

    public static IReadOnlyList<OnboardingOption> InterestOptions { get; } = new[]
    {
        new OnboardingOption("Photography", "📷 Photography"),
        new OnboardingOption("Architecture", "🏛️ Architecture"),
        new OnboardingOption("History", "📜 History"),
        new OnboardingOption("Art", "🎨 Art"),
        new OnboardingOption("Nature", "🌿 Nature"),
        new OnboardingOption("Shopping", "🛍️ Shopping"),
        new OnboardingOption("Nightlife", "🌃 Nightlife"),
        new OnboardingOption("Wellness", "🧘 Wellness"),
        new OnboardingOption("Adventure", "🧭 Adventure"),
        new OnboardingOption("Food & Drink", "🍽️ Food & Drink"),
        new OnboardingOption("Music", "🎵 Music"),
        new OnboardingOption("Sports", "⚽ Sports"),
    };

    public static IReadOnlyList<OnboardingOption> AvoidOptions { get; } = new[]
    {
        new OnboardingOption("Crowded Places", "👥 Crowded Places"),
        new OnboardingOption("Tourist Traps", "🎟️ Tourist Traps"),
        new OnboardingOption("Loud Venues", "🔊 Loud Venues"),
        new OnboardingOption("High-End Luxury", "💎 High-End Luxury"),
        new OnboardingOption("Budget Hostels", "🛏️ Budget Hostels"),
        new OnboardingOption("Religious Sites", "🛕 Religious Sites"),
        new OnboardingOption("Night Markets", "🌙 Night Markets"),
    };

    /// <summary>Spice slider stops, index 0 to 5, rendered under the MudSlider.</summary>
    public static IReadOnlyList<string> SpiceLabels { get; } = new[]
    {
        "Mild",
        "A little warmth",
        "Medium",
        "Hot",
        "Very hot",
        "Fiery",
    };

    // ---- The traveler's answers --------------------------------------------------------

    public string DisplayName { get; set; } = string.Empty;

    public string TravelStyle { get; set; } = TravelStyles[1].Value;

    public string EnergyPattern { get; set; } = EnergyPatterns[3].Value;

    public HashSet<string> DietaryRestrictions { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int SpiceTolerance { get; set; } = 2;

    public HashSet<string> FavoriteCuisines { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> DiningVibe { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> CoreInterests { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> HardAvoids { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string BudgetComfort { get; set; } = BudgetComforts[1].Value;

    public string Mobility { get; set; } = MobilityOptions[0].Value;

    public string TravelGroup { get; set; } = TravelGroups[0].Value;

    public bool FirstTimeVisitor { get; set; } = true;

    /// <summary>The wizard only needs a name; everything else has a sensible default.</summary>
    public bool IsNameProvided => !string.IsNullOrWhiteSpace(DisplayName);

    public string SpiceLabel => SpiceLabels[Math.Clamp(SpiceTolerance, 0, SpiceLabels.Count - 1)];

    /// <summary>Flips a value in one of the multi-choice sets, keeping it authoritative.</summary>
    public static void Toggle(HashSet<string> set, string value)
    {
        if (!set.Remove(value))
        {
            set.Add(value);
        }
    }

    // ---- Persistence ---------------------------------------------------------------------

    /// <summary>Reads an existing profile back into the draft for the edit wizard.</summary>
    public static TravelerOnboardingDraft From(UserProfile profile)
    {
        var draft = new TravelerOnboardingDraft
        {
            DisplayName = profile.DisplayName,
            SpiceTolerance = Math.Clamp(profile.SpiceTolerance, 0, SpiceLabels.Count - 1),
            FirstTimeVisitor = profile.FirstTimeVisitor,
        };

        draft.TravelStyle = Match(TravelStyles, profile.TravelStyle);
        draft.EnergyPattern = Match(EnergyPatterns, profile.EnergyPattern);
        draft.BudgetComfort = Match(BudgetComforts, profile.BudgetComfort);
        draft.Mobility = Match(MobilityOptions, profile.Mobility);
        draft.TravelGroup = Match(TravelGroups, profile.TravelGroup);

        // Multi-selects fall back to the agent-facing fields for profiles saved before onboarding.
        draft.DietaryRestrictions.UnionWith(MatchAll(DietaryOptions, profile.DietaryRestrictions, profile.DietaryRules));
        draft.FavoriteCuisines.UnionWith(MatchAll(Cuisines, profile.FavoriteCuisines));
        draft.DiningVibe.UnionWith(MatchAll(DiningVibes, profile.DiningVibe));
        draft.CoreInterests.UnionWith(MatchAll(InterestOptions, profile.CoreInterests, profile.Interests));
        draft.HardAvoids.UnionWith(MatchAll(AvoidOptions, profile.HardAvoids, profile.AvoidList));

        return draft;
    }

    /// <summary>
    /// Folds the captured answers into the profile the agents read, preserving the destination
    /// fields the wizard does not ask for.
    /// </summary>
    public UserProfile BuildProfile(
        string profileId,
        string destination,
        string? homeBase,
        string? attireNote,
        bool onboardingCompleted,
        bool pendingWelcome)
    {
        var dietary = DietaryRestrictions.Where(value => !string.Equals(value, "None", StringComparison.OrdinalIgnoreCase));

        return new UserProfile
        {
            Id = profileId,
            DisplayName = DisplayName.Trim(),
            Destination = destination,
            HomeBase = homeBase,
            AttireNote = attireNote,

            // Agent-facing fields: mapped from the richer onboarding answers.
            DietaryRules = dietary.ToArray(),
            Interests = CoreInterests.ToArray(),
            AvoidList = HardAvoids.ToArray(),
            Pacing = Pacing(),
            Budget = Budget(),
            GroupComposition = Group(),
            TravelerProfile = FirstTimeVisitor ? Domain.TravelerProfile.FirstTime : Domain.TravelerProfile.Experienced,

            // The captured preferences themselves.
            TravelStyle = TravelStyle,
            EnergyPattern = EnergyPattern,
            DietaryRestrictions = DietaryRestrictions.ToArray(),
            SpiceTolerance = Math.Clamp(SpiceTolerance, 0, SpiceLabels.Count - 1),
            FavoriteCuisines = FavoriteCuisines.ToArray(),
            DiningVibe = DiningVibe.ToArray(),
            CoreInterests = CoreInterests.ToArray(),
            HardAvoids = HardAvoids.ToArray(),
            BudgetComfort = BudgetComfort,
            Mobility = Mobility,
            TravelGroup = TravelGroup,
            FirstTimeVisitor = FirstTimeVisitor,
            OnboardingCompleted = onboardingCompleted,
            PendingWelcome = pendingWelcome,
        };
    }

    /// <summary>How unhurried the traveler wants the day to be.</summary>
    public PacingPreference Pacing() => TravelStyle switch
    {
        "Slow Explorer" => Domain.PacingPreference.Slow,
        "Efficient" => Domain.PacingPreference.Fast,
        "Cultural Immersion" => Domain.PacingPreference.Slow,
        _ => Domain.PacingPreference.Moderate,
    };

    /// <summary>How much money the traveler is comfortable spending.</summary>
    public BudgetRange Budget() => BudgetComfort switch
    {
        "Budget-Conscious" => Domain.BudgetRange.Low,
        "Money No Object" => Domain.BudgetRange.High,
        _ => Domain.BudgetRange.Medium,
    };

    /// <summary>The traveling party, as the agents know it.</summary>
    public GroupComposition Group() => TravelGroup switch
    {
        "Couple" => Domain.GroupComposition.Couple,
        "Family with Kids" => Domain.GroupComposition.Family,
        "Friends Group" => Domain.GroupComposition.Friends,
        _ => Domain.GroupComposition.Solo,
    };

    private static string Match(IReadOnlyList<OnboardingOption> catalog, string? stored) =>
        catalog.FirstOrDefault(option => string.Equals(option.Value, stored, StringComparison.OrdinalIgnoreCase))?.Value
        ?? catalog[0].Value;

    /// <summary>
    /// Lines the stored values up with the wizard's options. Values saved before onboarding
    /// existed (free text like "old town souks") are kept verbatim so re-saving never drops them.
    /// </summary>
    private static IEnumerable<string> MatchAll(
        IReadOnlyList<OnboardingOption> catalog,
        params IReadOnlyList<string>[] storedLists)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var stored in storedLists)
        {
            foreach (var value in stored)
            {
                var match = catalog.FirstOrDefault(option =>
                    string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase));

                var keep = match?.Value ?? value;

                if (!string.IsNullOrWhiteSpace(keep) && seen.Add(keep))
                {
                    yield return keep;
                }
            }
        }
    }
}
