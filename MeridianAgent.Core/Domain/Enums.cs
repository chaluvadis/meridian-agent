namespace MeridianAgent.Domain;

/// <summary>What the user is asking about right now.</summary>
public enum QueryIntent
{
    Food,
    Sightseeing,
    Transport,
    Shopping,
    Nightlife,
    Relaxation,
    Emergency,
}

public enum PacingPreference
{
    Slow,
    Moderate,
    Fast,
}

public enum EnergyLevel
{
    Low,
    Medium,
    High,
}

public enum TimeSensitivity
{
    Urgent,
    Normal,
    Flexible,
}

public enum BudgetRange
{
    Low,
    Medium,
    High,
}

public enum TravelerProfile
{
    FirstTime,
    Experienced,
}

public enum GroupComposition
{
    Solo,
    Couple,
    Family,
    Friends,
}

public enum VenueType
{
    Indoor,
    Outdoor,
    Mixed,
}

public enum VenueCategory
{
    Restaurant,
    Cafe,
    Museum,
    Park,
    Market,
    Activity,
}

public enum RiskSeverity
{
    High,
    Medium,
    Low,
}

public enum RuleCategory
{
    DressCode,
    Photography,
    Behavior,
    Alcohol,
    Pda,
    Other,
}

public enum SafeguardStatus
{
    Cleared,
    Flagged,
}
