namespace MeridianAgent.Infrastructure.Scheduling;

/// <summary>
/// Remembers what has already been said recently, so the agent does not repeat itself. Two
/// schedulers need the same rule — a suggestion cycle and a weather pivot — and both key on a
/// signature of the thing being said rather than on time alone.
/// </summary>
public sealed class SignatureCooldown
{
    private readonly List<Signature> _signatures = new();
    private readonly TimeSpan _rememberFor;

    public SignatureCooldown(TimeSpan rememberFor) => _rememberFor = rememberFor;

    public bool IsOnCooldown(string signature)
    {
        Prune();
        return _signatures.Any(entry =>
            string.Equals(entry.Value, signature, StringComparison.OrdinalIgnoreCase));
    }

    public void Remember(string signature)
    {
        Prune();
        _signatures.Add(new Signature(signature, DateTime.UtcNow));
    }

    private void Prune()
    {
        var cutoff = DateTime.UtcNow - _rememberFor;
        _signatures.RemoveAll(entry => entry.SeenAtUtc < cutoff);
    }

    private sealed record Signature(string Value, DateTime SeenAtUtc);
}
