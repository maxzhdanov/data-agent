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
        var hits = await _rules.RetrieveIsoRulesAsync(question, 1, cancellationToken);
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
                keep_alive = "5m",
                options = new { num_predict = _options.NumPredict, temperature = 0.1 },
                prompt = $"You are the FinGuard local audit assistant. Use only this policy context:\n{context}\n\n" +
                         $"{request}\nDo not infer missing values or regulatory conclusions. Reply in one concise sentence."
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
