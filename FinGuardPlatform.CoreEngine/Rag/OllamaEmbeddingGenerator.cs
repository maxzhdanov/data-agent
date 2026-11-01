using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace FinGuardPlatform.CoreEngine.Rag;

public sealed class OllamaEmbeddingGenerator : ITextEmbeddingGenerator
{
    private readonly HttpClient _http;
    private readonly EmbeddingOptions _options;

    public OllamaEmbeddingGenerator(HttpClient http, IOptions<EmbeddingOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<float[]>> GenerateAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken)
    {
        if (texts.Count == 0)
        {
            return [];
        }

        using var response = await _http.PostAsJsonAsync(
            "/api/embed",
            new { model = _options.Model, input = texts },
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Ollama embedding request failed with {(int)response.StatusCode}: {detail}");
        }

        var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken);
        if (result?.Embeddings is null || result.Embeddings.Count != texts.Count ||
            result.Embeddings.Any(embedding => embedding.Length == 0))
        {
            throw new InvalidDataException(
                $"Ollama embedding model '{_options.Model}' returned an invalid embedding count or empty vector.");
        }

        var dimensions = result.Embeddings[0].Length;
        if (result.Embeddings.Any(embedding => embedding.Length != dimensions))
        {
            throw new InvalidDataException("Ollama returned embedding vectors with inconsistent dimensions.");
        }

        return result.Embeddings;
    }

    private sealed class EmbeddingResponse
    {
        public List<float[]>? Embeddings { get; set; }
    }
}
