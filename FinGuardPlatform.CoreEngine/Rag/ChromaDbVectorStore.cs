using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinGuardPlatform.CoreEngine.Rag;

public sealed class ChromaDbVectorStore : IVectorStore
{
    private readonly HttpClient _http;
    private readonly ChromaOptions _options;
    private readonly ILogger<ChromaDbVectorStore> _logger;

    public ChromaDbVectorStore(HttpClient http, IOptions<ChromaOptions> options, ILogger<ChromaDbVectorStore> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        if (_http.BaseAddress is null && Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out var uri))
        {
            _http.BaseAddress = uri;
        }
    }

    public async Task UpsertAsync(IEnumerable<AuditDocumentChunk> chunks, CancellationToken cancellationToken)
    {
        var payload = chunks.ToList();
        if (payload.Count == 0)
        {
            return;
        }

        var body = new
        {
            ids = payload.Select(c => c.Id).ToArray(),
            documents = payload.Select(c => c.Text).ToArray(),
            metadatas = payload.Select(c => new Dictionary<string, string>(c.Metadata)
            {
                ["source"] = c.Source,
                ["title"] = c.Title
            }).ToArray()
        };

        var response = await _http.PostAsJsonAsync(
            $"/api/v2/tenants/{_options.Tenant}/databases/{_options.Database}/collections/{_options.Collection}/upsert",
            body,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Chroma upsert returned {Status}", response.StatusCode);
        }
    }

    public async Task<IReadOnlyList<SemanticHit>> QueryAsync(string collection, string query, int topK, CancellationToken cancellationToken)
    {
        var request = new { query_texts = new[] { query }, n_results = topK };
        using var response = await _http.PostAsJsonAsync(
            $"/api/v2/tenants/{_options.Tenant}/databases/{_options.Database}/collections/{collection}/query",
            request,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Chroma query returned {Status}; returning empty hits.", response.StatusCode);
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var hits = new List<SemanticHit>();
        if (!doc.RootElement.TryGetProperty("documents", out var documents) || documents.GetArrayLength() == 0)
        {
            return hits;
        }

        var docs = documents[0];
        var ids = doc.RootElement.TryGetProperty("ids", out var idNode) ? idNode[0] : default;
        var distances = doc.RootElement.TryGetProperty("distances", out var distNode) ? distNode[0] : default;
        for (var i = 0; i < docs.GetArrayLength(); i++)
        {
            hits.Add(new SemanticHit
            {
                Distance = distances.ValueKind == JsonValueKind.Array ? distances[i].GetDouble() : 0,
                Chunk = new AuditDocumentChunk
                {
                    Id = ids.ValueKind == JsonValueKind.Array ? ids[i].GetString() ?? Guid.NewGuid().ToString("N") : Guid.NewGuid().ToString("N"),
                    Collection = collection,
                    Text = docs[i].GetString() ?? string.Empty,
                    Source = "chromadb"
                }
            });
        }

        return hits;
    }
}

public sealed class AuditRuleRetriever : IAuditRuleRetriever
{
    private readonly IVectorStore _store;
    private readonly ChromaOptions _options;

    public AuditRuleRetriever(IVectorStore store, IOptions<ChromaOptions> options)
    {
        _store = store;
        _options = options.Value;
    }

    public Task<IReadOnlyList<SemanticHit>> RetrieveIsoRulesAsync(string question, int topK, CancellationToken cancellationToken) =>
        _store.QueryAsync(_options.Collection, question, topK, cancellationToken);
}
