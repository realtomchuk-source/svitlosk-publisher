using System;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Application.Interfaces;
using SvitloSk.Publisher.Application.Orchestration;

namespace SvitloSk.Publisher.Infrastructure.Channels.WhatsApp;

/// <summary>
/// Controls WhatsApp API rate limiting, request pacing, and backoff.
/// Enforces safe delays (800-1000ms) between consecutive calls to avoid Meta Cloud API throttles.
/// </summary>
public class WhatsAppRateLimiter
{
    private readonly IDelayProvider _delayProvider;
    private readonly int _throttleDelayMs;

    public WhatsAppRateLimiter(IDelayProvider? delayProvider = null, int throttleDelayMs = 800)
    {
        _delayProvider = delayProvider ?? new SystemDelayProvider();
        _throttleDelayMs = throttleDelayMs;
    }

    public async Task ThrottleAsync(CancellationToken cancellationToken = default)
    {
        await _delayProvider.DelayAsync(_throttleDelayMs, cancellationToken).ConfigureAwait(false);
    }

    public int CalculateBackoffDelayMs(int attempt, int? retryAfterSeconds = null)
    {
        if (retryAfterSeconds.HasValue)
        {
            int delaySec = Math.Min(retryAfterSeconds.Value, 60);
            return delaySec * 1000;
        }

        return attempt switch
        {
            1 => 1000,
            2 => 2000,
            _ => 4000
        };
    }

    public async Task DelayBackoffAsync(int attempt, int? retryAfterSeconds = null, CancellationToken cancellationToken = default)
    {
        int delayMs = CalculateBackoffDelayMs(attempt, retryAfterSeconds);
        await _delayProvider.DelayAsync(delayMs, cancellationToken).ConfigureAwait(false);
    }
}
