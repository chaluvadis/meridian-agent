using System.Text.Json;
using MeridianAgent.Application.Abstractions;
using MeridianAgent.Application.DTOs;
using MeridianAgent.Domain;
using MeridianAgent.Domain.AgentContracts;
using MeridianAgent.Domain.Entities;
using MeridianAgent.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace MeridianAgent.Infrastructure.AI.Agents;

/// <summary>
/// Agent 2: proposes 1-3 venues that fit the extracted constraints AND the current reality
/// (weather, local time, energy). Model: qwen2.5:3b.
/// </summary>
public sealed class SearchAgent : AgentBase, ISearchAgent
{
    public const string DefaultModel = "qwen2.5:3b";

    private const string Prompt = """
        You are a hyper-personalized travel planning agent. Your job is to propose 1 to 3 venues or activities that perfectly match the user's extracted constraints AND the current reality of their environment.

        PHILOSOPHY: "Reality hits different." If the weather is extreme, the user is tired, or it's late at night, you MUST adapt. Never suggest something that would make the user's experience worse.

        <extracted_constraints>
        $ExtractedConstraintsJson
        </extracted_constraints>

        <current_reality>
        {
          "location": "$CurrentLocation",
          "local_time": "$LocalTime",
          "weather_temp_c": $Temperature,
          "weather_condition": "$WeatherCondition",
          "is_raining": $IsRaining,
          "nearby_venues": $NearbyVenuesJson
        }
        </current_reality>

        STRICT RULES:
        1. Output ONLY a valid JSON array. No markdown, no explanations.
        2. Propose between 1 and 3 options. Never more than 3.
        3. REALITY CHECK:
           - If temperature > 35°C OR is_raining = true → ALL suggestions MUST be indoor/sheltered.
           - If local_time is between 23:00 and 06:00 → only suggest venues open at that hour.
           - If energy_level = "low" → prioritize venues within 10 minutes travel, minimal walking.
        4. PERSONALIZATION CHECK:
           - Every suggestion MUST satisfy ALL relevant_dietary constraints.
           - Every suggestion MUST align with relevant_interests.
           - Every suggestion MUST NOT appear in avoid_list.
        5. If NO venue satisfies all constraints, output an empty array with a reason. Never compromise on hard constraints (dietary, avoid_list).

        OUTPUT FORMAT (exact):
        [
          {
            "venue_name": "string",
            "venue_type": "indoor | outdoor | mixed",
            "category": "restaurant | cafe | museum | park | market | activity",
            "travel_time_minutes": 0,
            "why_it_fits": "One concise sentence explaining why this matches the user's constraints and current situation.",
            "estimated_duration": "string",
            "best_time_to_visit": "string"
          }
        ]

        If no suitable venue exists:
        {
          "error": true,
          "reason": "string explaining why no venue fits"
        }
        """;

    public SearchAgent(
        ILlmClient client,
        ILogger<SearchAgent> logger,
        string? model = null,
        string? sanitizerModel = null)
        : base(client, logger, model ?? DefaultModel)
    {
        SanitizerModel = sanitizerModel ?? DefaultModel;
    }

    public override string AgentName => "SearchAgent";

    public override string SanitizerModel { get; }

    public Task<ProposedItinerary> ProposeAsync(
        ExtractedConstraints constraints,
        TravelRequest request,
        CancellationToken cancellationToken = default)
    {
        var context = request.Context;
        var prompt = PromptRenderer.Render(
            Prompt,
            new Dictionary<string, string>
            {
                ["ExtractedConstraintsJson"] = JsonElementReader.Serialize(constraints),
                ["CurrentLocation"] = context?.Location ?? request.Profile.Destination,
                ["LocalTime"] = context?.LocalTime ?? "unknown",
                ["Temperature"] = JsonElementReader.ToInvariant(context?.TemperatureC ?? 0),
                ["WeatherCondition"] = context?.WeatherCondition ?? "unknown",
                ["IsRaining"] = JsonElementReader.ToJsonLiteral(context?.IsRaining ?? false),
                ["NearbyVenuesJson"] = JsonElementReader.Serialize(request.NearbyVenues),
            });

        return RunJsonAsync(
            prompt,
            element => ParseItinerary(element),
            cancellationToken,
            allowErrorObject: true);
    }

    private static ParseOutcome<ProposedItinerary> ParseItinerary(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            JsonExtractor.TryGetErrorMessage(root, out var reason))
        {
            throw new NoVenuesFoundException(reason ?? "the search agent found nothing that fits");
        }

        if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() == 0)
        {
            throw new NoVenuesFoundException("nothing nearby fits your non-negotiables");
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            return ParseOutcome<ProposedItinerary>.Fail("expected a JSON array of venues");
        }

        var errors = new List<string>();
        var venues = new List<Venue>();

        var index = 0;
        foreach (var item in root.EnumerateArray())
        {
            index++;
            if (item.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"venue #{index} must be a JSON object");
                continue;
            }

            var name = JsonElementReader.ReadString(item, "venue_name", errors, required: true);
            var type = JsonElementReader.ReadEnum<VenueType>(item, "venue_type", errors, required: true);
            var category = JsonElementReader.ReadEnum<VenueCategory>(item, "category", errors, required: true);
            if (name is null || type is null || category is null)
            {
                continue;
            }

            venues.Add(new Venue
            {
                Name = name,
                VenueType = type.Value,
                Category = category.Value,
                TravelTimeMinutes = JsonElementReader.ReadInt(item, "travel_time_minutes", errors),
                WhyItFits = JsonElementReader.ReadString(item, "why_it_fits", errors, required: false) ?? string.Empty,
                EstimatedDuration = JsonElementReader.ReadString(item, "estimated_duration", errors, required: false) ?? string.Empty,
                BestTimeToVisit = JsonElementReader.ReadString(item, "best_time_to_visit", errors, required: false) ?? string.Empty,
            });

            if (venues.Count > 3)
            {
                errors.Add("at most 3 venues may be proposed");
                break;
            }
        }

        if (errors.Count > 0)
        {
            return ParseOutcome<ProposedItinerary>.Fail(errors.ToArray());
        }

        if (venues.Count == 0)
        {
            throw new NoVenuesFoundException("the proposed plan contained no venues");
        }

        return ParseOutcome<ProposedItinerary>.Ok(new ProposedItinerary(venues));
    }
}
