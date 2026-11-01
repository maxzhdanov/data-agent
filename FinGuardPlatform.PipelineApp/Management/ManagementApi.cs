using System.Text.Json;
using System.Net.Http.Json;
using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Confluent.Kafka;
using FinGuardPlatform.CoreEngine.Contracts;
using FinGuardPlatform.CoreEngine.Storage;
using FinGuardPlatform.PipelineApp.Configuration;
using FinGuardPlatform.PipelineApp.Ingestion;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Polly.Timeout;

namespace FinGuardPlatform.PipelineApp.Management;

public static class ManagementApi
{
    public static void MapManagementApi(this WebApplication app)
    {
        app.MapGet("/api/health", () => Results.Ok(new { status = "ok", service = "pipeline" }));
        app.MapGet("/api/dependencies", async (DataBrowser browser, CancellationToken cancellationToken) =>
            await browser.GetDependencyStatusAsync(cancellationToken));
        app.MapGet("/api/runs", (IngestionWorker worker) => Results.Ok(new { runs = worker.GetRuns() }));
        app.MapGet("/api/runs/{runId}", (string runId, IngestionWorker worker) =>
            worker.TryGetRun(runId, out var run) ? Results.Ok(run) : Results.NotFound(new { error = "Run not found." }));
        app.MapGet("/api/schemas", async (SchemaCatalog schemas, CancellationToken cancellationToken) =>
            Results.Ok(new { schemas = await schemas.ListAsync(cancellationToken) }));
        app.MapGet("/api/schemas/{schemaId}", async (string schemaId, SchemaCatalog schemas, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await schemas.ReadByIdAsync(schemaId, cancellationToken));
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound(new { error = "Schema not found." });
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });
        app.MapPut("/api/schemas", async (FixedWidthSchema schema, SchemaCatalog schemas, CancellationToken cancellationToken) =>
        {
            try
            {
                await schemas.SaveAsync(schema, cancellationToken);
                return Results.Ok(new { schema.SchemaId, saved = true });
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
            catch (InvalidOperationException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });
        app.MapPost("/api/import", async (HttpRequest request, IngestionWorker worker, IOptions<IngestionOptions> options, CancellationToken cancellationToken) =>
        {
            if (!request.HasFormContentType)
            {
                return Results.BadRequest(new { error = "Upload must use multipart/form-data." });
            }

            var form = await request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new { error = "Choose a non-empty fixed-width file." });
            }

            if (file.Length > options.Value.MaximumUploadBytes)
            {
                return Results.BadRequest(new { error = $"Upload exceeds the {options.Value.MaximumUploadBytes / (1024 * 1024)} MB limit." });
            }

            var schemaId = form["schemaId"].ToString();
            if (string.IsNullOrWhiteSpace(schemaId))
            {
                return Results.BadRequest(new { error = "schemaId is required." });
            }

            if (!Enum.TryParse<StorageTarget>(form["storageTarget"], true, out var storageTarget) ||
                !Enum.IsDefined(storageTarget))
            {
                return Results.BadRequest(new { error = "storageTarget must be MongoDb or DynamoDbLocal." });
            }

            try
            {
                await using var stream = file.OpenReadStream();
                var run = await worker.QueueImportAsync(stream, file.FileName, schemaId, storageTarget, cancellationToken);
                return Results.Accepted($"/api/runs/{run.RunId}", run);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
            catch (FileNotFoundException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });

        app.MapGet("/api/storage/chroma/collections", async (DataBrowser browser, CancellationToken cancellationToken) =>
            await browser.GetChromaCollectionsAsync(cancellationToken));
        app.MapGet("/api/storage/chroma/collections/{collectionId}/documents", async (
            string collectionId,
            int? limit,
            int? offset,
            DataBrowser browser,
            CancellationToken cancellationToken) =>
            await browser.GetChromaDocumentsAsync(collectionId, limit ?? 50, offset ?? 0, cancellationToken));
        app.MapGet("/api/storage/dynamodb/tables", async (DataBrowser browser, CancellationToken cancellationToken) =>
            await browser.GetDynamoTablesAsync(cancellationToken));
        app.MapGet("/api/storage/dynamodb/tables/{tableName}/items", async (
            string tableName,
            int? limit,
            int? offset,
            DataBrowser browser,
            CancellationToken cancellationToken) =>
            await browser.GetDynamoItemsAsync(tableName, limit ?? 50, offset ?? 0, cancellationToken));
        app.MapGet("/api/storage/mongodb/collections", async (DataBrowser browser, CancellationToken cancellationToken) =>
            await browser.GetMongoCollectionsAsync(cancellationToken));
        app.MapGet("/api/storage/mongodb/collections/{collectionName}/documents", async (
            string collectionName,
            int? limit,
            DataBrowser browser,
            CancellationToken cancellationToken) =>
            await browser.GetMongoDocumentsAsync(collectionName, limit ?? 50, cancellationToken));
    }
}

public sealed class DataBrowser : IDisposable
{
    private readonly StorageOptions _storage;
    private readonly string _chromaTenant;
    private readonly string _chromaDatabase;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DataBrowser> _logger;
    private readonly MongoClient _mongoClient;
    private readonly IMongoDatabase _mongoDatabase;
    private readonly IAmazonDynamoDB _dynamoClient;

    public DataBrowser(
        IOptions<StorageOptions> storage,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<DataBrowser> logger)
    {
        _storage = storage.Value;
        _chromaTenant = configuration["Chroma:Tenant"] ?? "default_tenant";
        _chromaDatabase = configuration["Chroma:Database"] ?? "default_database";
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
        _mongoClient = new MongoClient(_storage.Mongo.ConnectionString);
        _mongoDatabase = _mongoClient.GetDatabase(_storage.Mongo.Database);
        _dynamoClient = CreateDynamoClient();
    }

    public async Task<IResult> GetDependencyStatusAsync(CancellationToken cancellationToken)
    {
        var checks = await Task.WhenAll(
            ProbeAsync("Kafka", ProbeKafkaAsync, cancellationToken),
            ProbeAsync("Ollama", ProbeOllamaAsync, cancellationToken),
            ProbeAsync("ChromaDB", ProbeChromaAsync, cancellationToken),
            ProbeAsync("DynamoDB Local", ProbeDynamoAsync, cancellationToken),
            ProbeAsync("MongoDB", ProbeMongoAsync, cancellationToken),
            ProbeAsync("DuckDB API", ProbeDuckDbAsync, cancellationToken));
        return Results.Ok(new { services = checks, allHealthy = checks.All(check => check.Healthy) });
    }

    public async Task<IResult> GetChromaCollectionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await ChromaClient().GetAsync(ChromaCollectionsPath, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return await UpstreamError(response, cancellationToken);
            }

            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return ChromaError("ChromaDB returned an unexpected collections response.");
            }

            var collections = new List<object>();
            foreach (var collection in document.RootElement.EnumerateArray())
            {
                if (!collection.TryGetProperty("id", out var idElement) ||
                    !collection.TryGetProperty("name", out var nameElement) ||
                    idElement.ValueKind != JsonValueKind.String ||
                    nameElement.ValueKind != JsonValueKind.String)
                {
                    return ChromaError("A ChromaDB collection response is missing its string id or name.");
                }

                int? count = collection.TryGetProperty("count", out var countElement) &&
                             countElement.TryGetInt32(out var parsedCount)
                    ? parsedCount
                    : null;
                collections.Add(new { id = idElement.GetString(), name = nameElement.GetString(), count });
            }

            return Results.Ok(new { collections });
        }
        catch (HttpRequestException exception)
        {
            return ChromaError(exception.Message);
        }
        catch (JsonException exception)
        {
            return ChromaError($"ChromaDB returned invalid JSON: {exception.Message}");
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            return ChromaError($"The ChromaDB request timed out: {exception.Message}");
        }
        catch (TimeoutRejectedException exception)
        {
            return ChromaError($"The ChromaDB request timed out: {exception.Message}");
        }
    }

    public async Task<IResult> GetChromaDocumentsAsync(string collectionId, int limit, int offset, CancellationToken cancellationToken)
    {
        if (!ValidPage(limit, offset))
        {
            return Results.BadRequest(new { error = "limit must be 1-200 and offset 0-10000." });
        }

        var path = $"{ChromaCollectionsPath}/{Uri.EscapeDataString(collectionId)}/get";
        try
        {
            using var response = await ChromaClient().PostAsJsonAsync(
                path,
                new { limit, offset, include = new[] { "documents", "metadatas" } },
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return await UpstreamError(response, cancellationToken);
            }

            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            var root = document.RootElement;
            return Results.Ok(new
            {
                ids = ReadArray(root, "ids"),
                documents = ReadArray(root, "documents"),
                metadatas = ReadArray(root, "metadatas")
            });
        }
        catch (HttpRequestException exception)
        {
            return ChromaError(exception.Message);
        }
        catch (JsonException exception)
        {
            return ChromaError($"ChromaDB returned invalid JSON: {exception.Message}");
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            return ChromaError($"The ChromaDB request timed out: {exception.Message}");
        }
        catch (TimeoutRejectedException exception)
        {
            return ChromaError($"The ChromaDB request timed out: {exception.Message}");
        }
    }

    public async Task<IResult> GetDynamoTablesAsync(CancellationToken cancellationToken)
    {
        var result = await _dynamoClient.ListTablesAsync(cancellationToken);
        return Results.Ok(new { tables = result.TableNames });
    }

    public async Task<IResult> GetDynamoItemsAsync(string tableName, int limit, int offset, CancellationToken cancellationToken)
    {
        if (!ValidPage(limit, offset))
        {
            return Results.BadRequest(new { error = "limit must be 1-200 and offset 0-10000." });
        }

        var tables = await _dynamoClient.ListTablesAsync(cancellationToken);
        if (!tables.TableNames.Contains(tableName, StringComparer.Ordinal))
        {
            return Results.NotFound(new { error = "DynamoDB table not found." });
        }

        var items = new List<Dictionary<string, AttributeValue>>();
        Dictionary<string, AttributeValue>? startKey = null;
        var remaining = offset + limit;
        while (remaining > 0)
        {
            var page = await _dynamoClient.ScanAsync(new ScanRequest
            {
                TableName = tableName,
                Limit = Math.Min(remaining, 200),
                ExclusiveStartKey = startKey ?? new Dictionary<string, AttributeValue>()
            }, cancellationToken);
            items.AddRange(page.Items);
            startKey = page.LastEvaluatedKey;
            remaining = Math.Max(0, offset + limit - items.Count);
            if (startKey is not { Count: > 0 } || page.Items.Count == 0)
            {
                break;
            }
        }

        var rows = items.Skip(offset).Take(limit).Select(item => item.ToDictionary(
            pair => pair.Key,
            pair => DynamoValue(pair.Value)));
        return Results.Ok(new { table = tableName, rows, offset, limit, hasMore = startKey is { Count: > 0 } });
    }

    public async Task<IResult> GetMongoCollectionsAsync(CancellationToken cancellationToken)
    {
        var names = await (await _mongoDatabase.ListCollectionNamesAsync(cancellationToken: cancellationToken)).ToListAsync(cancellationToken);
        return Results.Ok(new { collections = names });
    }

    public async Task<IResult> GetMongoDocumentsAsync(string collectionName, int limit, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 200)
        {
            return Results.BadRequest(new { error = "limit must be 1-200." });
        }

        var names = await (await _mongoDatabase.ListCollectionNamesAsync(cancellationToken: cancellationToken)).ToListAsync(cancellationToken);
        if (!names.Contains(collectionName, StringComparer.Ordinal))
        {
            return Results.NotFound(new { error = "MongoDB collection not found." });
        }

        var documents = await _mongoDatabase.GetCollection<BsonDocument>(collectionName)
            .Find(FilterDefinition<BsonDocument>.Empty)
            .Limit(limit)
            .ToListAsync(cancellationToken);
        return Results.Ok(new { collection = collectionName, documents = documents.Select(document => document.ToJson()) });
    }

    private string ChromaCollectionsPath =>
        $"/api/v2/tenants/{Uri.EscapeDataString(_chromaTenant)}/databases/{Uri.EscapeDataString(_chromaDatabase)}/collections";

    private HttpClient ChromaClient() => _httpClientFactory.CreateClient("chroma-browser");

    private IAmazonDynamoDB CreateDynamoClient()
    {
        var config = new AmazonDynamoDBConfig
        {
            ServiceURL = _storage.Dynamo.ServiceUrl,
            AuthenticationRegion = _storage.Dynamo.Region,
            UseHttp = _storage.Dynamo.ServiceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        };
        return new AmazonDynamoDBClient(
            new BasicAWSCredentials(_storage.Dynamo.AccessKey, _storage.Dynamo.SecretKey),
            config);
    }

    private async Task<string> ProbeKafkaAsync(CancellationToken cancellationToken)
    {
        var config = new AdminClientConfig
        {
            BootstrapServers = _configuration["Kafka:BootstrapServers"] ?? "localhost:9092"
        };
        var metadata = await Task.Run(() =>
        {
            using var client = new AdminClientBuilder(config).Build();
            return client.GetMetadata(TimeSpan.FromSeconds(2));
        }, cancellationToken);
        return $"{metadata.Brokers.Count} broker(s) reachable";
    }

    private async Task<string> ProbeOllamaAsync(CancellationToken cancellationToken) =>
        await ProbeHttpAsync("ollama-health", "/api/tags", cancellationToken);

    private async Task<string> ProbeChromaAsync(CancellationToken cancellationToken)
    {
        using var response = await ChromaClient().GetAsync(ChromaCollectionsPath, cancellationToken);
        response.EnsureSuccessStatusCode();
        return "API reachable";
    }

    private async Task<string> ProbeDynamoAsync(CancellationToken cancellationToken)
    {
        var tables = await _dynamoClient.ListTablesAsync(cancellationToken);
        return $"{tables.TableNames.Count} table(s)";
    }

    private async Task<string> ProbeMongoAsync(CancellationToken cancellationToken)
    {
        await _mongoDatabase.RunCommandAsync<BsonDocument>(
            new BsonDocument("ping", 1),
            cancellationToken: cancellationToken);
        return "Database reachable";
    }

    private async Task<string> ProbeDuckDbAsync(CancellationToken cancellationToken) =>
        await ProbeHttpAsync("duckdb-health", "/api/health", cancellationToken);

    private async Task<string> ProbeHttpAsync(string clientName, string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClientFactory.CreateClient(clientName).GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        return "API reachable";
    }

    private static async Task<ServiceStatus> ProbeAsync(
        string name,
        Func<CancellationToken, Task<string>> probe,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        try
        {
            return new ServiceStatus(name, true, await probe(timeout.Token));
        }
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested &&
            (exception is HttpRequestException or
                OperationCanceledException or
                TimeoutException or
                AmazonServiceException or
                MongoException or
                KafkaException))
        {
            return new ServiceStatus(name, false, exception.Message);
        }
    }

    private static bool ValidPage(int limit, int offset) => limit is >= 1 and <= 200 && offset is >= 0 and <= 10000;

    private static object? DynamoValue(AttributeValue value)
    {
        if (value.S is not null) return value.S;
        if (value.N is not null) return decimal.TryParse(value.N, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var number) ? number : value.N;
        if (value.IsBOOLSet) return value.BOOL;
        if (value.NULL == true) return null;
        if (value.SS is not null) return value.SS;
        if (value.NS is not null) return value.NS;
        if (value.L is not null) return value.L.Select(DynamoValue).ToArray();
        if (value.M is not null) return value.M.ToDictionary(pair => pair.Key, pair => DynamoValue(pair.Value));
        return "(binary)";
    }

    private static object?[] ReadArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return array.EnumerateArray().Select(value => value.ValueKind switch
        {
            JsonValueKind.Object => (object?)value.Clone(),
            JsonValueKind.Array => value.Clone(),
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        }).ToArray();
    }

    private static async Task<IResult> UpstreamError(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        return Results.Json(new
        {
            error = "ChromaDB request failed.",
            status = (int)response.StatusCode,
            detail
        }, statusCode: StatusCodes.Status502BadGateway);
    }

    private IResult ChromaError(string detail)
    {
        _logger.LogWarning("ChromaDB browser request failed: {Detail}", detail);
        return Results.Json(new
        {
            error = "Could not read ChromaDB collections or documents.",
            detail
        }, statusCode: StatusCodes.Status502BadGateway);
    }

    private sealed record ServiceStatus(string Name, bool Healthy, string Detail);

    public void Dispose()
    {
        _dynamoClient.Dispose();
        _mongoClient.Dispose();
    }
}
