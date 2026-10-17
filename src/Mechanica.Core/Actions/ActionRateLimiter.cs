namespace Mechanica.Core.Actions;

/// <summary>
/// Token bucket per caller peer, so one client can't flood the host with actions. Each peer starts
/// with <see cref="Burst"/> tokens, a call spends one, and tokens refill at <see cref="PerSecond"/>.
/// </summary>
public sealed class ActionRateLimiter
{
    private readonly Dictionary<int, (double Tokens, double Time)> _buckets = [];

    /// <summary>Creates a limiter.</summary>
    public ActionRateLimiter(double perSecond = 60, double burst = 120)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(perSecond);
        ArgumentOutOfRangeException.ThrowIfLessThan(burst, 1);
        PerSecond = perSecond;
        Burst = burst;
    }

    /// <summary>Sustained calls per second.</summary>
    public double PerSecond { get; }

    /// <summary>Calls allowed at once after a quiet period.</summary>
    public double Burst { get; }

    /// <summary>Spends a token for <paramref name="peer"/> at time <paramref name="now"/> (seconds); false when none is left.</summary>
    public bool TryAcquire(int peer, double now)
    {
        var (tokens, time) = _buckets.TryGetValue(peer, out var bucket) ? bucket : (Burst, now);
        tokens = Math.Min(Burst, tokens + Math.Max(0, now - time) * PerSecond);
        var allowed = tokens >= 1;
        _buckets[peer] = (allowed ? tokens - 1 : tokens, now);
        return allowed;
    }

    /// <summary>Forgets a peer (when it leaves).</summary>
    public void Forget(int peer) => _buckets.Remove(peer);
}
