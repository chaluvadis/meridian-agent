using MeridianAgent.Application.Abstractions;
using MeridianAgent.Application.DTOs;
using MeridianAgent.Application.State;
using MeridianAgent.Domain.AgentContracts;
using MeridianAgent.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace MeridianAgent.Application.Orchestrator;

/// <summary>
/// Coordinates the four-agent pipeline: Context → Search → Safeguard → Synthesizer.
/// Agent outputs are published to the shared <see cref="Blackboard"/> after each stage.
/// Every stage degrades gracefully: a failure yields a user-friendly fallback instead of an exception.
/// </summary>
public sealed class TravelQueryOrchestrator
{
    private readonly IContextAgent _contextAgent;
    private readonly ISearchAgent _searchAgent;
    private readonly ISafeguardAgent _safeguardAgent;
    private readonly ISynthesizerAgent _synthesizerAgent;
    private readonly IRulebookNarrower? _rulebookNarrower;
    private readonly ILogger<TravelQueryOrchestrator> _logger;

    public TravelQueryOrchestrator(
        IContextAgent contextAgent,
        ISearchAgent searchAgent,
        ISafeguardAgent safeguardAgent,
        ISynthesizerAgent synthesizerAgent,
        ILogger<TravelQueryOrchestrator> logger,
        IRulebookNarrower? rulebookNarrower = null,
        AgentBlackboard? blackboard = null)
    {
        _contextAgent = contextAgent;
        _searchAgent = searchAgent;
        _safeguardAgent = safeguardAgent;
        _synthesizerAgent = synthesizerAgent;
        _logger = logger;
        _rulebookNarrower = rulebookNarrower;
        Blackboard = blackboard ?? new AgentBlackboard();
    }

    public AgentBlackboard Blackboard { get; }

    /// <summary>Streams the Synthesizer response token-by-token.</summary>
    public async IAsyncEnumerable<string> StreamAsync(
        TravelRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Blackboard.Clear();
        Blackboard.PublishQuery(request.Query);

        _logger.LogInformation("Pipeline started: \"{Query}\" for {Destination}", request.Query, request.Profile.Destination);

        ExtractedConstraints? constraints = null;
        try
        {
            constraints = await _contextAgent.ExtractAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Context agent failed");
        }

        if (constraints is null)
        {
            yield return "I'm having trouble reading your preferences right now. Please try that again in a moment.";
            yield break;
        }

        Blackboard.PublishConstraints(constraints);
        _logger.LogInformation(
            "Context agent extracted intent {Intent}, energy {EnergyLevel}",
            constraints.QueryIntent,
            constraints.EnergyLevel);

        NoVenuesFoundException? noVenues = null;
        string? searchFailure = null;
        ProposedItinerary plan = ProposedItinerary.Empty;

        try
        {
            plan = await _searchAgent.ProposeAsync(constraints, request, cancellationToken).ConfigureAwait(false);
            Blackboard.PublishItinerary(plan);
        }
        catch (NoVenuesFoundException ex)
        {
            noVenues = ex;
            Blackboard.PublishItinerary(ProposedItinerary.Empty);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            searchFailure = "I couldn't build a plan just now. Give me another try in a moment.";
            _logger.LogError(ex, "Search agent failed");
        }

        if (noVenues is not null)
        {
            _logger.LogInformation("Search agent found no venues: {Reason}", noVenues.Reason);
            yield return
                $"I checked what's around you, but nothing fits your non-negotiables right now — {noVenues.Reason} " +
                "Want me to widen the search, or plan something indoors instead?";
            yield break;
        }

        if (searchFailure is not null)
        {
            yield return searchFailure;
            yield break;
        }

        SafeguardReport report = SafeguardReport.Cleared;
        try
        {
            var safeguardRequest = await NarrowRulebookAsync(request, cancellationToken).ConfigureAwait(false);
            report = await _safeguardAgent.CheckAsync(plan, safeguardRequest, cancellationToken).ConfigureAwait(false);
            Blackboard.PublishSafeguardReport(report);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Safeguard agent failed; continuing with a cleared report");
            Blackboard.PublishSafeguardReport(SafeguardReport.Cleared);
        }

        var synthesisRequest = new SynthesisRequest(
            request.Query,
            plan,
            report,
            request.Profile.DisplayName);

        var response = new System.Text.StringBuilder();
        await foreach (var token in _synthesizerAgent
            .StreamAsync(synthesisRequest, cancellationToken)
            .ConfigureAwait(false))
        {
            response.Append(token);
            yield return token;
        }

        Blackboard.PublishResponse(response.ToString());
        _logger.LogInformation("Pipeline completed with {VenueCount} venue(s)", plan.Venues.Count);
    }

    /// <summary>Runs the full pipeline and returns the complete response text.</summary>
    public async Task<TravelResponse> CompleteAsync(
        TravelRequest request,
        CancellationToken cancellationToken = default)
    {
        var text = new System.Text.StringBuilder();
        await foreach (var token in StreamAsync(request, cancellationToken).ConfigureAwait(false))
        {
            text.Append(token);
        }

        return new TravelResponse(
            text.ToString(),
            Blackboard.Itinerary ?? ProposedItinerary.Empty,
            Blackboard.SafeguardReport ?? SafeguardReport.Cleared);
    }

    private async Task<TravelRequest> NarrowRulebookAsync(TravelRequest request, CancellationToken cancellationToken)
    {
        if (_rulebookNarrower is null || request.Rulebook.Rules.Count == 0)
        {
            return request;
        }

        try
        {
            var narrowed = await _rulebookNarrower
                .NarrowAsync(request.Rulebook, request.Query, cancellationToken)
                .ConfigureAwait(false);

            return narrowed.Rules.Count == 0
                ? request
                : request with { Rulebook = narrowed };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rulebook narrowing failed; using the full rulebook");
            return request;
        }
    }
}
