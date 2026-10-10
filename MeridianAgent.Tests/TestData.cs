using MeridianAgent.Application.Abstractions;
using MeridianAgent.Application.DTOs;
using MeridianAgent.Domain;
using MeridianAgent.Domain.AgentContracts;
using MeridianAgent.Domain.Entities;
using MeridianAgent.Domain.Exceptions;

namespace MeridianAgent.Tests;

/// <summary>Shared fixtures: a traveler in Kyoto at 21:40 in the rain.</summary>
public static class TestData
{
    public static UserProfile Profile => new()
    {
        Id = "default",
        DisplayName = "Aria",
        Destination = "Kyoto, Japan",
        TravelerProfile = TravelerProfile.FirstTime,
        GroupComposition = GroupComposition.Couple,
        DietaryRules = new[] { "vegetarian" },
        Interests = new[] { "temples", "quiet cafés" },
        AvoidList = new[] { "crowded nightclubs" },
        Pacing = PacingPreference.Moderate,
        Budget = BudgetRange.Medium,
        AttireNote = "light scarf in bag",
        HomeBase = "Kyoto Station",
    };

    public static TravelContext Context => new()
    {
        Location = "Kyoto, Japan",
        LocalTime = "21:40",
        TemperatureC = 31,
        WeatherCondition = "Rain",
        IsRaining = true,
        WeatherSummary = "Warm evening rain, 31°C",
    };

    public static IReadOnlyList<NearbyVenue> NearbyVenues => new[]
    {
        new NearbyVenue
        {
            Name = "Ippodo Tea Room",
            Category = "cafe",
            IsOpenNow = true,
            IsIndoor = true,
            DietaryOptions = new[] { "vegetarian" },
            DistanceMinutes = 6,
        },
        new NearbyVenue
        {
            Name = "Gion Night Walk",
            Category = "activity",
            IsOpenNow = true,
            IsIndoor = false,
            DistanceMinutes = 12,
        },
        new NearbyVenue
        {
            Name = "Pontocho Alley Food Crawl",
            Category = "restaurant",
            IsOpenNow = false,
            IsIndoor = false,
            DietaryOptions = new[] { "vegetarian" },
            DistanceMinutes = 9,
        },
    };

    public static DestinationRulebook Rulebook => new()
    {
        Destination = "Kyoto, Japan",
        Rules = new[]
        {
            new SafeguardRule
            {
                RuleId = "kyoto-photo-1",
                Category = RuleCategory.Photography,
                Rule = "Photography is prohibited inside the main hall of Kiyomizu-dera.",
                Severity = RiskSeverity.Medium,
                GentleFix = "Keep phones pocketed inside the hall; the veranda is fine for photos.",
            },
            new SafeguardRule
            {
                RuleId = "kyoto-dress-1",
                Category = RuleCategory.DressCode,
                Rule = "Shoulders covered at major temples.",
                Severity = RiskSeverity.Low,
                GentleFix = "A light scarf handles it.",
            },
        },
    };

    public static TravelRequest Request(string query = "We're exhausted - anywhere good for dinner?") => new(
        query,
        Profile,
        Context,
        NearbyVenues,
        Rulebook);

    public static ExtractedConstraints Constraints => new()
    {
        QueryIntent = QueryIntent.Food,
        RelevantDietary = new[] { "vegetarian" },
        RelevantInterests = new[] { "temples", "quiet cafés" },
        AvoidList = new[] { "crowded nightclubs" },
        PacingPreference = PacingPreference.Slow,
        EnergyLevel = EnergyLevel.Low,
        TimeSensitivity = TimeSensitivity.Normal,
        BudgetRange = BudgetRange.Medium,
    };

    public static ProposedItinerary Itinerary => new(new[]
    {
        new Venue
        {
            Name = "Ippodo Tea Room",
            VenueType = VenueType.Indoor,
            Category = VenueCategory.Cafe,
            TravelTimeMinutes = 6,
            WhyItFits = "Warm, quiet, fully vegetarian - a six minute walk on a rainy night.",
            EstimatedDuration = "45-60 minutes",
            BestTimeToVisit = "Tonight, before it gets busy",
        },
    });

    public static SafeguardReport FlaggedReport => new(
        SafeguardStatus.Flagged,
        new[]
        {
            new RiskWarning
            {
                AppliesToVenue = "Ippodo Tea Room",
                Severity = RiskSeverity.Low,
                RuleCategory = RuleCategory.DressCode,
                RiskDescription = "Shoulders covered at major temples.",
                GentleFix = "A light scarf handles it.",
            },
        });
}

public sealed class FakeContextAgent : IContextAgent
{
    public List<TravelRequest> Requests { get; } = new();

    public ExtractedConstraints Result { get; set; } = TestData.Constraints;

    public Exception? Error { get; set; }

    public Task<ExtractedConstraints> ExtractAsync(TravelRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        if (Error is not null)
        {
            throw Error;
        }

        return Task.FromResult(Result);
    }
}

public sealed class FakeSearchAgent : ISearchAgent
{
    public List<TravelRequest> Requests { get; } = new();

    public ProposedItinerary Result { get; set; } = TestData.Itinerary;

    public NoVenuesFoundException? NoVenuesError { get; set; }

    public Exception? Error { get; set; }

    public Task<ProposedItinerary> ProposeAsync(
        ExtractedConstraints constraints,
        TravelRequest request,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        if (NoVenuesError is not null)
        {
            throw NoVenuesError;
        }

        if (Error is not null)
        {
            throw Error;
        }

        return Task.FromResult(Result);
    }
}

public sealed class FakeSafeguardAgent : ISafeguardAgent
{
    public SafeguardReport Result { get; set; } = SafeguardReport.Cleared;

    public Exception? Error { get; set; }

    public Task<SafeguardReport> CheckAsync(
        ProposedItinerary plan,
        TravelRequest request,
        CancellationToken cancellationToken = default)
    {
        if (Error is not null)
        {
            throw Error;
        }

        return Task.FromResult(Result);
    }
}

public sealed class FakeSynthesizerAgent : ISynthesizerAgent
{
    public List<SynthesisRequest> Requests { get; } = new();

    public string[] Chunks { get; set; } = new[] { "Top ", "pick: ", "Ippodo Tea Room." };

    public IAsyncEnumerable<string> StreamAsync(SynthesisRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return Stream();

        async IAsyncEnumerable<string> Stream()
        {
            foreach (var chunk in Chunks)
            {
                await Task.Yield();
                yield return chunk;
            }
        }
    }

    public async Task<string> CompleteAsync(SynthesisRequest request, CancellationToken cancellationToken = default)
    {
        var builder = new System.Text.StringBuilder();
        await foreach (var chunk in StreamAsync(request, cancellationToken).ConfigureAwait(false))
        {
            builder.Append(chunk);
        }

        return builder.ToString();
    }
}
