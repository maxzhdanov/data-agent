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
    Task<string?> CommentOnDeadLetterAsync(DeadLetterPayload payload, CancellationToken cancellationToken);
}

public sealed class OllamaAuditClient : IOllamaAuditClient
{
    private readonly HttpClient _http;
    private readonly OllamaOptions _options;
    private readonly OllamaInferenceLimiter _limiter;
    private readonly ResiliencePipeline _pipeline;
    private readonly IAuditRuleRetriever _rules;
    public OllamaAuditClient(
        HttpClient http,
        IOptions<OllamaOptions> options,
        OllamaInferenceLimiter limiter,
        ResiliencePipelineProvider<string> pipelines,
        IAuditRuleRetriever rules)
    {
        _http = http;
        _options = options.Value;
        _limiter = limiter;
        _pipeline = pipelines.GetPipeline(ResilienceRegistration.Ollama);
        _rules = rules;
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

        var context = await RetrieveContextAsync(
            $"Audit payment record schema {record.SchemaId} for ISO 25012 completeness and ISO 27001 access control.",
            cancellationToken);
        return await GenerateAsync(
            $"Summarize residual data-quality risk for record {record.CorrelationId} with fields {string.Join(',', record.Fields.Keys)}.",
            context,
            cancellationToken);
    }

    public async Task<string?> CommentOnDeadLetterAsync(DeadLetterPayload payload, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return null;
        }

        var context = await RetrieveContextAsync(
            $"Analyze fixed-width import validation errors for schema {payload.SchemaId}. Focus on completeness, accuracy, and recoverability.",
            cancellationToken);
        var safeErrors = string.Join("; ", payload.Errors);
        return await GenerateAsync(
            $"A record was rejected at line {payload.LineNumber} for schema {payload.SchemaId}. Validation errors: {safeErrors}. " +
            "Explain likely format issues and safe source-system corrections. Do not request or infer raw account values.",
            context,
            cancellationToken);
    }

    private async Task<string> RetrieveContextAsync(string question, CancellationToken cancellationToken)
    {
        var hits = await _rules.RetrieveIsoRulesAsync(question, 3, cancellationToken);
        return string.Join("\n", hits.Select(hit => hit.Chunk.Text));
    }

    private async Task<string> GenerateAsync(string request, string context, CancellationToken cancellationToken)
    {
        using var lease = await _limiter.AcquireAsync(cancellationToken);
        return await _pipeline.ExecuteAsync(async ct =>
        {
            using var response = await _http.PostAsJsonAsync("/api/generate", new
            {
                model = _options.Model,
                stream = false,
                prompt = $"You are the FinGuard local audit assistant. Use only this retrieved policy context:\n{context}\n\n{request}"
            }, ct);

            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(ct);
            return string.IsNullOrWhiteSpace(payload?.Response)
                ? throw new InvalidDataException("Ollama returned an empty audit response.")
                : payload.Response;
        }, cancellationToken);
    }

    private sealed class OllamaGenerateResponse
    {
        public string? Response { get; set; }
    }
}
