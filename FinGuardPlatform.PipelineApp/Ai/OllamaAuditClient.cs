using System.Net.Http.Json;
using FinGuardPlatform.CoreEngine.Contracts;
using FinGuardPlatform.CoreEngine.Rag;
using FinGuardPlatform.PipelineApp.Configuration;
using FinGuardPlatform.PipelineApp.RateLimiting;
using FinGuardPlatform.PipelineApp.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;

namespace FinGuardPlatform.PipelineApp.Ai;

public interface IOllamaAuditClient
{
    Task<string?> CommentAsync(CleanRecord record, CancellationToken cancellationToken);
}

public sealed class OllamaAuditClient : IOllamaAuditClient
{
    private readonly HttpClient _http;
    private readonly OllamaOptions _options;
    private readonly OllamaInferenceLimiter _limiter;
    private readonly ResiliencePipeline _pipeline;
    private readonly IAuditRuleRetriever _rules;
    private readonly ILogger<OllamaAuditClient> _logger;

    public OllamaAuditClient(
        HttpClient http,
        IOptions<OllamaOptions> options,
        OllamaInferenceLimiter limiter,
        ResiliencePipelineProvider<string> pipelines,
        IAuditRuleRetriever rules,
        ILogger<OllamaAuditClient> logger)
    {
        _http = http;
        _options = options.Value;
        _limiter = limiter;
        _pipeline = pipelines.GetPipeline(ResilienceRegistration.Ollama);
        _rules = rules;
        _logger = logger;
        if (_http.BaseAddress is null)
        {
            _http.BaseAddress = new Uri(_options.BaseUrl);
        }
    }

    public async Task<string?> CommentAsync(CleanRecord record, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return null;
        }

        using var lease = await _limiter.AcquireAsync(cancellationToken);
        var hits = await _rules.RetrieveIsoRulesAsync(
            $"Audit payment record schema {record.SchemaId} for ISO 25012 completeness and ISO 27001 access control.",
            3,
            cancellationToken);
        var context = string.Join("\n", hits.Select(h => h.Chunk.Text));

        try
        {
            return await _pipeline.ExecuteAsync(async ct =>
            {
                using var response = await _http.PostAsJsonAsync("/api/generate", new
                {
                    model = _options.Model,
                    stream = false,
                    prompt = $"You are the FinGuard local audit agent. Using only this retrieved policy context:\n{context}\n\nSummarize residual data-quality risk for record {record.CorrelationId} with fields {string.Join(',', record.Fields.Keys)}."
                }, ct);

                response.EnsureSuccessStatusCode();
                var payload = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(ct);
                return payload?.Response;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ollama inference skipped after retry/circuit-breaker.");
            return null;
        }
    }

    private sealed class OllamaGenerateResponse
    {
        public string? Response { get; set; }
    }
}
