using System.Threading.RateLimiting;
using FinGuardPlatform.PipelineApp.Configuration;
using Microsoft.Extensions.Options;

namespace FinGuardPlatform.PipelineApp.RateLimiting;

public sealed class OllamaInferenceLimiter : IDisposable
{
    private readonly TokenBucketRateLimiter _limiter;

    public OllamaInferenceLimiter(IOptions<RateLimitingOptions> options)
    {
        var o = options.Value;
        _limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = o.TokenLimit,
            TokensPerPeriod = o.TokensPerPeriod,
            ReplenishmentPeriod = TimeSpan.FromSeconds(Math.Max(1, o.ReplenishmentPeriodSeconds)),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = o.QueueLimit,
            AutoReplenishment = true
        });
    }

    public async Task<RateLimitLease> AcquireAsync(CancellationToken cancellationToken)
    {
        var lease = await _limiter.AcquireAsync(1, cancellationToken);
        if (!lease.IsAcquired)
        {
            throw new InvalidOperationException("Ollama inference rate limit exceeded; text-block processing paused to protect host compute.");
        }

        return lease;
    }

    public void Dispose() => _limiter.Dispose();
}
