using System.Collections.Concurrent;

namespace DrSharonKellyEnt.Forms;

// Simple in-memory sliding-window rate limiter, keyed by "formType:ip" so the
// contact form and referral form each get their own bucket. Fine for a single
// IIS instance; move to a shared store (SQL/Redis) if this ever runs behind a
// load balancer across multiple servers.
public class RateLimiter
{
    private readonly RateLimitOptions _options;
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _hits = new();
    private readonly object _lock = new();

    public RateLimiter(Microsoft.Extensions.Options.IOptions<RateLimitOptions> options) => _options = options.Value;

    public bool Allow(string key)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var window = TimeSpan.FromMinutes(_options.WindowMinutes);
            var queue = _hits.GetOrAdd(key, _ => new Queue<DateTime>());

            while (queue.Count > 0 && now - queue.Peek() > window)
                queue.Dequeue();

            if (queue.Count >= _options.MaxRequestsPerIpPerWindow)
                return false;

            queue.Enqueue(now);
            return true;
        }
    }
}
