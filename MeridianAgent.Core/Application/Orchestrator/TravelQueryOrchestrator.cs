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

    /// <summary>
    /// Everything the pipeline produced up to the Synthesizer. <see cref="EarlyText"/> is set when a
    /// stage failed and the traveler should see a friendly message instead of an answer.
    /// </summary>
    private sealed record PipelineStage(
        string? EarlyText,
        SynthesisRequest? SynthesisRequest,
        ProposedItinerary Itinerary,
        SafeguardReport Report);

    /// <summary>Streams the Synthesizer response token-by-token.</summary>
    public async IAsyncEnumerable<string> StreamAsync(
        TravelRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var stage = await PrepareAsync(request, cancellationToken).ConfigureAwait(false);

        if (stage.EarlyText is not null)
        {
            yield return stage.EarlyText;
            yield break;
        }

        var response = new System.Text.StringBuilder();
        await foreach (var token in _synthesizerAgent
            .StreamAsync(stage.SynthesisRequest!, cancellationToken)
            .ConfigureAwait(false))
        {
            response.Append(token);
            yield return token;
        }

        Blackboard.PublishResponse(response.ToString());
        _logger.LogInformation("Pipeline completed with {VenueCount} venue(s)", stage.Itinerary.Venues.Count);
    }

    /// <summary>
    /// Runs the pipeline and returns the message together with its one-tap quick actions, so the chat
    /// can render a reply the traveler can act on without typing. Same stages, same degradation.
    /// </summary>
    public async Task<TravelResponse> SynthesizeAsync(
        TravelRequest request,
        CancellationToken cancellationToken = default)
    {
        var stage = await PrepareAsync(request, cancellationToken).ConfigureAwait(false);

        if (stage.EarlyText is not null)
        {
            return new TravelResponse(
                stage.EarlyText,
                stage.Itinerary,
                stage.Report,
                Array.Empty<QuickAction>());
        }

        SynthesisResponse synthesis;

        try
        {
            synthesis = await _synthesizerAgent
                .SynthesizeAsync(stage.SynthesisRequest!, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Synthesizer agent failed");
            synthesis = SynthesisResponse.Empty;
        }

        var text = synthesis.Message;

        if (string.IsNullOrWhiteSpace(text))
        {
            text = "I couldn't put an answer together just now. Give me another try in a moment.";
        }

        Blackboard.PublishResponse(text);
        _logger.LogInformation(
            "Pipeline completed with {VenueCount} venue(s) and {ActionCount} quick action(s)",
            stage.Itinerary.Venues.Count,
            synthesis.QuickActions.Count);

        return new TravelResponse(text, stage.Itinerary, stage.Report, synthesis.QuickActions);
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
            Blackboard.SafeguardReport ?? SafeguardReport.Cleared,
            Array.Empty<QuickAction>());
    }

    /// <summary>
    /// Context → Search → Safeguard, blackboard publication included. Returns the friendly fallback
    /// text when a stage fails, otherwise the request the Synthesizer answers.
    /// </summary>
    private async Task<PipelineStage> PrepareAsync(TravelRequest request, CancellationToken cancellationToken)
    {
        Blackboard.Clear();
        Blackboard.PublishQuery(request.Query);

        // The profile is normalized once, here, so every downstream agent and serializer sees
        // present lists: a null list is the one defect that can turn a working pipeline into the
        // generic "trouble reading your preferences" fallback.
        request = request with { Profile = (request.Profile ?? UserProfile.Default).WithSafeLists() };

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
            // AgentInvocationException already carries the last raw model response, so this single
            // line is the whole diagnosis: the message, the stack, and what the model actually said.
            _logger.LogError(
                ex,
                "Context agent failed; the Context stage could not read the traveler profile (destination {Destination})",
                request.Profile.Destination);
        }

        if (constraints is null)
        {
            // The profile stage is the one place a traveler cannot self-serve, so the message says
            // what they can do about it. The technical detail stays in the log above.
            _logger.LogWarning("Context agent returned no constraints; returning the profile hiccup fallback");
            return new PipelineStage(
                "I'm having a brief technical hiccup reading your profile. Could you try asking your question again, or visit the settings to update your preferences?",
                null,
                ProposedItinerary.Empty,
                SafeguardReport.Cleared);
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
            _logger.LogInformation("Strict search found nothing ({Reason}); retrying with soft constraints relaxed", ex.Reason);

            ProposedItinerary? relaxedPlan = null;
            try
            {
                relaxedPlan = await _searchAgent
                    .ProposeAsync(RelaxConstraints(constraints), request, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (NoVenuesFoundException relaxedEx)
            {
                noVenues = relaxedEx;
                Blackboard.PublishItinerary(ProposedItinerary.Empty);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception relaxedFailure)
            {
                searchFailure = "I couldn't build a plan just now. Give me another try in a moment.";
                _logger.LogError(relaxedFailure, "Search agent failed during the relaxed retry");
            }

            if (relaxedPlan is not null)
            {
                plan = relaxedPlan;
                Blackboard.PublishItinerary(plan);
                _logger.LogInformation("Relaxed search produced {VenueCount} venue(s)", plan.Venues.Count);
            }
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

            return new PipelineStage(
                $"I checked what's around you, but nothing fits your non-negotiables right now — {noVenues.Reason} " +
                "Want me to widen the search, or plan something indoors instead?",
                null,
                ProposedItinerary.Empty,
                SafeguardReport.Cleared);
        }

        if (searchFailure is not null)
        {
            return new PipelineStage(searchFailure, null, plan, SafeguardReport.Cleared);
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

        return new PipelineStage(null, synthesisRequest, plan, report);
    }

    /// <summary>
    /// Drops the soft constraints (interests, pacing, energy, timing, budget) that a small model
    /// tends to over-enforce, keeping only the hard ones: dietary rules and the avoid list.
    /// </summary>
    private static ExtractedConstraints RelaxConstraints(ExtractedConstraints constraints) => constraints with
    {
        RelevantInterests = null,
        PacingPreference = PacingPreference.Fast,
        EnergyLevel = EnergyLevel.Medium,
        TimeSensitivity = TimeSensitivity.Flexible,
        BudgetRange = null,
    };

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
