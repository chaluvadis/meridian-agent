namespace MeridianAgent.Infrastructure.AI;

using System.ClientModel;

using Microsoft.Extensions.AI;

using Microsoft.Extensions.Diagnostics.HealthChecks;

/// <summary>
/// Proves the model key actually works, so a deployment with a rejected, revoked, or empty-balance
/// key fails its health gate instead of looking green until a traveler asks a question. Without
/// this, a bad key boots the container healthy and the first user query returns the pipeline's
/// generic "technical hiccup" fallback.
/// </summary>
public sealed class GatewayHealthCheck : IHealthCheck
{
    /// <summary>How often a probe is actually sent: one failure must show, but a 30-second poll
    /// should not buy tokens 120 times an hour.</summary>
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(60);

    private readonly IChatClient _chatClient;
    private readonly ILogger<GatewayHealthCheck> _logger;
    private readonly Lock _gate = new();

    private HealthStatus _lastStatus = HealthStatus.Healthy;
    private string _lastReason = "not probed yet";
    private DateTimeOffset _lastProbe = DateTimeOffset.MinValue;

    public GatewayHealthCheck(IChatClient chatClient, ILogger<GatewayHealthCheck> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (DateTimeOffset.UtcNow - _lastProbe < ProbeInterval)
            {
                return Build(_lastStatus, _lastReason);
            }

            _lastProbe = DateTimeOffset.UtcNow;
        }

        try
        {
            // One token of reply: the cheapest call that still proves the gateway accepts the key.
            await _chatClient
                .GetResponseAsync("ping", new ChatOptions { MaxOutputTokens = 1 }, cancellationToken)
                .ConfigureAwait(false);

            _lastStatus = HealthStatus.Healthy;
            _lastReason = "the gateway answered a probe";
        }
        catch (ClientResultException ex)
        {
            // 401: rejected key. 402 on the gateway means a zero balance — both mean every query
            // fails, so the container is not healthy.
            var fatal = ex.Status is 401 or 402;
            _lastStatus = fatal ? HealthStatus.Unhealthy : HealthStatus.Degraded;
            _lastReason = $"the gateway answered HTTP {ex.Status}: {ex.Message}";

            if (fatal)
            {
                _logger.LogError(ex, "The Kilo gateway returned HTTP {Status} for the configured API key", ex.Status);
            }
            else
            {
                _logger.LogWarning(ex, "The Kilo gateway returned HTTP {Status} for a probe", ex.Status);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Unreachable or timed out: degraded rather than unhealthy, because a network hiccup
            // says nothing about the key and the next traveler query may well succeed.
            _lastStatus = HealthStatus.Degraded;
            _lastReason = $"the gateway probe failed: {ex.Message}";
            _logger.LogWarning(ex, "The Kilo gateway probe failed");
        }

        return Build(_lastStatus, _lastReason);
    }

    private static HealthCheckResult Build(HealthStatus status, string reason) =>
        status switch
        {
            HealthStatus.Unhealthy => HealthCheckResult.Unhealthy(reason),
            HealthStatus.Degraded => HealthCheckResult.Degraded(reason),
            _ => HealthCheckResult.Healthy(reason),
        };
}
