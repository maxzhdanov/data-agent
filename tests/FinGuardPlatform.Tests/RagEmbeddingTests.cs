using System.Net;
using System.Text;
using System.Text.Json;
using FinGuardPlatform.CoreEngine.Rag;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinGuardPlatform.Tests;

public sealed class RagEmbeddingTests
{
    [Fact]
    public async Task OllamaEmbeddingGeneratorUsesConfiguredModelAndBatchInput()
    {
        string? requestBody = null;
        var client = new HttpClient(new StubHttpMessageHandler(async request =>
        {
            requestBody = await request.Content!.ReadAsStringAsync();
            return JsonResponse("""{"model":"nomic-embed-text","embeddings":[[0.1,0.2],[0.3,0.4]]}""");
        }))
        {
            BaseAddress = new Uri("http://ollama.local")
        };
        var generator = new OllamaEmbeddingGenerator(
            client,
            Options.Create(new EmbeddingOptions { Model = "nomic-embed-text" }));

        var vectors = await generator.GenerateAsync(["first rule", "second rule"], CancellationToken.None);

        Assert.Equal(2, vectors.Count);
        Assert.Equal(2, vectors[0].Length);
        using var request = JsonDocument.Parse(requestBody!);
        Assert.Equal("nomic-embed-text", request.RootElement.GetProperty("model").GetString());
        Assert.Equal(2, request.RootElement.GetProperty("input").GetArrayLength());
    }

    [Fact]
    public async Task ChromaQuerySendsQueryEmbeddingsInsteadOfQueryTexts()
    {
        string? queryBody = null;
        var client = new HttpClient(new StubHttpMessageHandler(async request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return JsonResponse("""[{"id":"collection-id","name":"iso-audit-rules"}]""");
            }

            queryBody = await request.Content!.ReadAsStringAsync();
            return JsonResponse("""{"ids":[["rule-1"]],"documents":[["Rule text"]],"distances":[[0.25]]}""");
        }))
        {
            BaseAddress = new Uri("http://chroma.local")
        };
        var store = new ChromaDbVectorStore(
            client,
            Options.Create(new ChromaOptions()),
            NullLogger<ChromaDbVectorStore>.Instance,
            new StubEmbeddingGenerator());

        var hits = await store.QueryAsync("iso-audit-rules", "check completeness", 3, CancellationToken.None);

        Assert.Single(hits);
        Assert.Equal("Rule text", hits[0].Chunk.Text);
        using var request = JsonDocument.Parse(queryBody!);
        Assert.True(request.RootElement.TryGetProperty("query_embeddings", out var embeddings));
        Assert.Equal(1, embeddings.GetArrayLength());
        Assert.False(request.RootElement.TryGetProperty("query_texts", out _));
    }

    [Fact]
    public async Task ChromaUpsertEmbedsDocumentsBeforeSendingThem()
    {
        string? upsertBody = null;
        var client = new HttpClient(new StubHttpMessageHandler(async request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return JsonResponse("[]");
            }

            if (request.RequestUri!.AbsolutePath.EndsWith("/collections", StringComparison.Ordinal))
            {
                return JsonResponse("""{"id":"created-collection"}""");
            }

            upsertBody = await request.Content!.ReadAsStringAsync();
            return JsonResponse("{}");
        }))
        {
            BaseAddress = new Uri("http://chroma.local")
        };
        var store = new ChromaDbVectorStore(
            client,
            Options.Create(new ChromaOptions()),
            NullLogger<ChromaDbVectorStore>.Instance,
            new StubEmbeddingGenerator());

        await store.UpsertAsync(
            [new AuditDocumentChunk { Id = "rule-1", Text = "Rule text" }],
            CancellationToken.None);

        using var requestBody = JsonDocument.Parse(upsertBody!);
        Assert.Equal("rule-1", requestBody.RootElement.GetProperty("ids")[0].GetString());
        var embeddings = requestBody.RootElement.GetProperty("embeddings");
        Assert.Equal(1, embeddings.GetArrayLength());
        Assert.Equal(0.1, embeddings[0][0].GetDouble(), precision: 5);
        Assert.Equal("Rule text", requestBody.RootElement.GetProperty("documents")[0].GetString());
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubEmbeddingGenerator : ITextEmbeddingGenerator
    {
        public Task<IReadOnlyList<float[]>> GenerateAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new[] { 0.1f, 0.2f }).ToArray());
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request);
    }
}
