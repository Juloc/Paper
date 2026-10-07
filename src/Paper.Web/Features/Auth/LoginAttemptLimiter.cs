using System.Collections.Concurrent;

namespace Paper.Web.Features.Auth;

public sealed class LoginAttemptLimiter
{
    public const int MaximumFailures = 8;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan Lockout = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, AttemptState> attempts = new(StringComparer.Ordinal);

    public bool TryBegin(string key, DateTimeOffset now, out TimeSpan retryAfter)
    {
        var state = attempts.GetOrAdd(NormalizeKey(key), static _ => new AttemptState());
        lock (state)
        {
            ResetWindowIfExpired(state, now);
            if (state.BlockedUntil is { } blockedUntil && blockedUntil > now)
            {
                retryAfter = blockedUntil - now;
                return false;
            }

            retryAfter = TimeSpan.Zero;
            return true;
        }
    }

    public void RecordFailure(string key, DateTimeOffset now)
    {
        var state = attempts.GetOrAdd(NormalizeKey(key), static _ => new AttemptState());
        lock (state)
        {
            ResetWindowIfExpired(state, now);
            state.Failures++;
            if (state.Failures >= MaximumFailures)
            {
                state.BlockedUntil = now.Add(Lockout);
            }
        }

        Prune(now);
    }

    public void RecordSuccess(string key) => attempts.TryRemove(NormalizeKey(key), out _);

    private void Prune(DateTimeOffset now)
    {
        if (attempts.Count <= 4096)
        {
            return;
        }

        foreach (var item in attempts)
        {
            lock (item.Value)
            {
                if (item.Value.BlockedUntil is null && item.Value.WindowStarted.Add(Window) <= now)
                {
                    attempts.TryRemove(new KeyValuePair<string, AttemptState>(item.Key, item.Value));
                }
            }
        }
    }

    private static void ResetWindowIfExpired(AttemptState state, DateTimeOffset now)
    {
        if (state.WindowStarted == default)
        {
            state.WindowStarted = now;
            return;
        }

        if (state.BlockedUntil is { } blockedUntil)
        {
            if (blockedUntil > now)
            {
                return;
            }

            state.WindowStarted = now;
            state.Failures = 0;
            state.BlockedUntil = null;
            return;
        }

        if (state.WindowStarted.Add(Window) <= now)
        {
            state.WindowStarted = now;
            state.Failures = 0;
        }
    }

    private static string NormalizeKey(string key) => string.IsNullOrWhiteSpace(key) ? "unknown" : key.Trim();

    private sealed class AttemptState
    {
        public DateTimeOffset WindowStarted { get; set; }
        public int Failures { get; set; }
        public DateTimeOffset? BlockedUntil { get; set; }
    }
}
