using FinGuardPlatform.CoreEngine.DependencyInjection;
using FinGuardPlatform.CoreEngine.State;
using FinGuardPlatform.PipelineApp.Ai;
using FinGuardPlatform.PipelineApp.Configuration;
using FinGuardPlatform.PipelineApp.Ingestion;
using FinGuardPlatform.PipelineApp.Messaging;
using FinGuardPlatform.PipelineApp.RateLimiting;
using FinGuardPlatform.PipelineApp.Resilience;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();

builder.Services.Configure<IngestionOptions>(builder.Configuration.GetSection("Ingestion"));
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
builder.Services.AddHostedService<IngestionWorker>();

var host = builder.Build();
await host.RunAsync();
