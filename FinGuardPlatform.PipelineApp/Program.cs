using FinGuardPlatform.CoreEngine.DependencyInjection;
using FinGuardPlatform.CoreEngine.State;
using FinGuardPlatform.PipelineApp.Ai;
using FinGuardPlatform.PipelineApp.Configuration;
using FinGuardPlatform.PipelineApp.Ingestion;
using FinGuardPlatform.PipelineApp.Management;
using FinGuardPlatform.PipelineApp.Rag;
using FinGuardPlatform.PipelineApp.Messaging;
using FinGuardPlatform.PipelineApp.RateLimiting;
using FinGuardPlatform.PipelineApp.Resilience;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["ASPNETCORE_URLS"] ?? "http://0.0.0.0:8081");
builder.AddServiceDefaults();

builder.Services.Configure<IngestionOptions>(builder.Configuration.GetSection("Ingestion"));
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 20 * 1024 * 1024;
});
builder.Services.Configure<KafkaOptions>(builder.Configuration.GetSection("Kafka"));
builder.Services.PostConfigure<KafkaOptions>(options =>
{
    var connection = builder.Configuration.GetConnectionString("kafka");
    if (!string.IsNullOrWhiteSpace(connection))
    {
        options.BootstrapServers = connection;
    }
});
builder.Services.PostConfigure<FinGuardPlatform.CoreEngine.Storage.StorageOptions>(options =>
{
    var mongo = builder.Configuration.GetConnectionString("mongo")
                ?? builder.Configuration.GetConnectionString("finguard");
    if (!string.IsNullOrWhiteSpace(mongo))
    {
        options.Mongo.ConnectionString = mongo;
    }
});
builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection("Ollama"));
builder.Services.Configure<RateLimitingOptions>(builder.Configuration.GetSection("RateLimiting"));

builder.Services.AddFinGuardCoreEngine(builder.Configuration);
builder.Services.AddFinGuardResilience(builder.Configuration);
builder.Services.AddSingleton<SchemaCatalog>();
builder.Services.AddSingleton<DataBrowser>();
builder.Services.AddHostedService<ChromaRuleSeeder>();
builder.Services.AddHttpClient("chroma-browser", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Chroma:BaseUrl"] ?? "http://localhost:8000");
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddHttpClient("ollama-health", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Ollama:BaseUrl"] ?? "http://localhost:11434");
    client.Timeout = TimeSpan.FromSeconds(3);
});
builder.Services.AddHttpClient("duckdb-health", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["DuckDb:BaseUrl"] ?? "http://localhost:8002");
    client.Timeout = TimeSpan.FromSeconds(3);
});
builder.Services.AddSingleton<OllamaInferenceLimiter>();
builder.Services.AddSingleton<IRecordPublisher, KafkaRecordPublisher>();
builder.Services.AddSingleton<IPipelineStateStore>(_ =>
{
    var path = builder.Configuration["PipelineStatePath"] ?? "data/pipeline-state.json";
    if (!Path.IsPathRooted(path))
    {
        path = Path.Combine(AppContext.BaseDirectory, path);
    }

    return new FilePipelineStateStore(path);
});
builder.Services.AddHttpClient<IOllamaAuditClient, OllamaAuditClient>((sp, client) =>
{
    var options = builder.Configuration.GetSection("Ollama").Get<OllamaOptions>() ?? new OllamaOptions();
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(45);
});
builder.Services.AddSingleton<IngestionWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<IngestionWorker>());

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapManagementApi();
await app.RunAsync();
