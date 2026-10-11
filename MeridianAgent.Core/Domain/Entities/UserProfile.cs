namespace MeridianAgent.Domain.Entities;

/// <summary>Traveler preferences the Context and Safeguard agents read from.</summary>
/// <remarks>
/// The first block of properties is the agent-facing contract the pipeline has always read:
/// <see cref="DietaryRules" />, <see cref="Interests" />, <see cref="AvoidList" />,
/// <see cref="Pacing" />, <see cref="Budget" />, <see cref="GroupComposition" /> and
/// <see cref="TravelerProfile" />. Onboarding writes those fields by mapping the richer
/// preferences it captures (the second block) onto them, so the agents keep working unchanged
/// while the profile carries the full picture the traveler actually gave us.
/// </remarks>
public sealed record UserProfile
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "default";

    [JsonPropertyName("display_name")]
    public string DisplayName { get; init; } = "Traveler";

    [JsonPropertyName("destination")]
    public string Destination { get; init; } = string.Empty;

    [JsonPropertyName("traveler_profile")]
    public Domain.TravelerProfile TravelerProfile { get; init; } = Domain.TravelerProfile.FirstTime;

    [JsonPropertyName("group_composition")]
    public Domain.GroupComposition GroupComposition { get; init; } = Domain.GroupComposition.Solo;

    [JsonPropertyName("dietary_rules")]
    public IReadOnlyList<string> DietaryRules { get; init; } = Array.Empty<string>();

    [JsonPropertyName("interests")]
    public IReadOnlyList<string> Interests { get; init; } = Array.Empty<string>();

    [JsonPropertyName("avoid_list")]
    public IReadOnlyList<string> AvoidList { get; init; } = Array.Empty<string>();

    [JsonPropertyName("pacing")]
    public Domain.PacingPreference Pacing { get; init; } = Domain.PacingPreference.Moderate;

    [JsonPropertyName("budget")]
    public Domain.BudgetRange Budget { get; init; } = Domain.BudgetRange.Medium;

    [JsonPropertyName("attire_note")]
    public string? AttireNote { get; init; }

    [JsonPropertyName("home_base")]
    public string? HomeBase { get; init; }

    /// <summary>
    /// Where the traveler actually is, when they have let the app find out. Nullable because it is
    /// opt-in: until they press the button, the app only knows the city they typed.
    /// </summary>
    [JsonPropertyName("latitude")]
    public double? Latitude { get; init; }

    [JsonPropertyName("longitude")]
    public double? Longitude { get; init; }

    // ---- Preferences captured by the onboarding wizard -------------------------------
    // Kept exactly as the traveler picked them; mapped onto the agent-facing fields above.

    [JsonPropertyName("travel_style")]
    public string TravelStyle { get; init; } = TravelerOnboardingDraft.TravelStyles[1].Value;

    [JsonPropertyName("energy_pattern")]
    public string EnergyPattern { get; init; } = TravelerOnboardingDraft.EnergyPatterns[3].Value;

    [JsonPropertyName("dietary_restrictions")]
    public IReadOnlyList<string> DietaryRestrictions { get; init; } = Array.Empty<string>();

    [JsonPropertyName("spice_tolerance")]
    public int SpiceTolerance { get; init; }

    [JsonPropertyName("favorite_cuisines")]
    public IReadOnlyList<string> FavoriteCuisines { get; init; } = Array.Empty<string>();

    [JsonPropertyName("dining_vibe")]
    public IReadOnlyList<string> DiningVibe { get; init; } = Array.Empty<string>();

    [JsonPropertyName("core_interests")]
    public IReadOnlyList<string> CoreInterests { get; init; } = Array.Empty<string>();

    [JsonPropertyName("hard_avoids")]
    public IReadOnlyList<string> HardAvoids { get; init; } = Array.Empty<string>();

    [JsonPropertyName("budget_comfort")]
    public string BudgetComfort { get; init; } = TravelerOnboardingDraft.BudgetComforts[1].Value;

    [JsonPropertyName("mobility")]
    public string Mobility { get; init; } = TravelerOnboardingDraft.MobilityOptions[0].Value;

    /// <summary>Onboarding label for the traveling party (mapped to <see cref="GroupComposition" />).</summary>
    [JsonPropertyName("travel_group")]
    public string TravelGroup { get; init; } = TravelerOnboardingDraft.TravelGroups[0].Value;

    [JsonPropertyName("first_time_visitor")]
    public bool FirstTimeVisitor { get; init; } = true;

    // ---- Lifecycle flags --------------------------------------------------------------

    /// <summary>True once the onboarding wizard has been completed; gates the /onboarding guard.</summary>
    [JsonPropertyName("onboarding_completed")]
    public bool OnboardingCompleted { get; init; }

    /// <summary>True right after onboarding so the chat can greet the traveler once.</summary>
    [JsonPropertyName("pending_welcome")]
    public bool PendingWelcome { get; init; }

    public static UserProfile Default => new();

    /// <summary>
    /// The same profile with every list guaranteed non-null. JSON (a hand-edited file, a row
    /// migrated from an older schema, an explicit <c>null</c> in an onboarding payload) can leave
    /// a list property null, and the first <c>.Count</c> on it becomes a NullReferenceException in
    /// front of the traveler. Reads through the stores and the pipeline entry point normalize with
    /// this, so downstream code can treat the lists as present.
    /// </summary>
    public UserProfile WithSafeLists() => this with
    {
        DietaryRules = DietaryRules ?? Array.Empty<string>(),
        Interests = Interests ?? Array.Empty<string>(),
        AvoidList = AvoidList ?? Array.Empty<string>(),
        DietaryRestrictions = DietaryRestrictions ?? Array.Empty<string>(),
        FavoriteCuisines = FavoriteCuisines ?? Array.Empty<string>(),
        DiningVibe = DiningVibe ?? Array.Empty<string>(),
        CoreInterests = CoreInterests ?? Array.Empty<string>(),
        HardAvoids = HardAvoids ?? Array.Empty<string>(),
    };

    /// <summary>
    /// The place this traveler is right now: their home base if they named one, otherwise the
    /// destination they travelled for. One rule, so the app bar, the chat header and the pipeline
    /// can never disagree about which city they are in.
    /// </summary>
    public string EffectiveLocation =>
        string.IsNullOrWhiteSpace(HomeBase) ? Destination : HomeBase!;

    /// <summary>
    /// The traveler's opt-in position, or null. Anything that can be measured — distance, driving
    /// time, the weather where they are standing rather than where the city centre is — reads this
    /// first and falls back to the name of the city when it is absent.
    /// </summary>
    [JsonIgnore]
    public GeoPoint? Coordinates =>
        Latitude is null || Longitude is null ? null : new GeoPoint(Latitude.Value, Longitude.Value);

    /// <summary>
    /// This profile's position, or null when they have not shared one. Kept separate from
    /// <see cref="Coordinates"/> so a caller can tell "no position known" from a placeholder.
    /// </summary>
    [JsonIgnore]
    public bool HasCoordinates => Coordinates is not null;

    /// <summary>
    /// Local, model-free greeting for the first chat after onboarding — used when the Synthesizer
    /// cannot produce one. Warm on purpose: the traveler should still feel recognized.
    /// </summary>
    public string BuildWelcomeFallback()
    {
        var interests = CoreInterests.Count > 0 ? CoreInterests : Interests;
        var avoids = HardAvoids.Count > 0 ? HardAvoids : AvoidList;
        var flavour = interests.Count > 0
            ? $"I've learned you're a {TravelStyle.ToLowerInvariant()} traveler who loves {string.Join(", ", interests.Take(3))}"
            : $"I've learned you're a {TravelStyle.ToLowerInvariant()} traveler";

        var avoid = avoids.Count > 0
            ? $", and that {string.Join(", ", avoids.Take(2))} stays off your list"
            : string.Empty;

        return $"Welcome, {DisplayName}! {flavour}{avoid}. Ask me anything — I'll keep it in mind.";
    }
}
