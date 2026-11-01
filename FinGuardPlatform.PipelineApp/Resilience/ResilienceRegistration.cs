using FinGuardPlatform.PipelineApp.Configuration;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace FinGuardPlatform.PipelineApp.Resilience;

public static class ResilienceRegistration
{
    public const string Kafka = "kafka";
    public const string Mongo = "mongodb";
    public const string Dynamo = "dynamodb";
    public const string Ollama = "ollama";

    public static IServiceCollection AddFinGuardResilience(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection("Resilience").Get<ResilienceSettings>() ?? new ResilienceSettings();

        services.AddResiliencePipeline(Kafka, pipeline => Configure(pipeline, settings));
        services.AddResiliencePipeline(Mongo, pipeline => Configure(pipeline, settings));
        services.AddResiliencePipeline(Dynamo, pipeline => Configure(pipeline, settings));
        services.AddResiliencePipeline(Ollama, pipeline => Configure(pipeline, settings));

        return services;
    }

    private static void Configure(ResiliencePipelineBuilder builder, ResilienceSettings settings)
    {
        builder.AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = settings.RetryCount,
            Delay = TimeSpan.FromSeconds(settings.RetryBackoffSeconds),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true
        });

        builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            MinimumThroughput = Math.Max(2, settings.CircuitBreakerFailures),
            BreakDuration = TimeSpan.FromSeconds(settings.CircuitBreakerBreakSeconds),
            SamplingDuration = TimeSpan.FromSeconds(30)
        });
    }
}
