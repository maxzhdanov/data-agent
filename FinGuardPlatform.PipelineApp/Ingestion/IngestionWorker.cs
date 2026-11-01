using FinGuardPlatform.CoreEngine.Contracts;
using FinGuardPlatform.CoreEngine.Parsing;
using FinGuardPlatform.CoreEngine.State;
using FinGuardPlatform.CoreEngine.Storage;
using FinGuardPlatform.CoreEngine.Validation;
using FinGuardPlatform.PipelineApp.Ai;
using FinGuardPlatform.PipelineApp.Configuration;
using FinGuardPlatform.PipelineApp.Messaging;
using FinGuardPlatform.PipelineApp.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;

namespace FinGuardPlatform.PipelineApp.Ingestion;

public sealed class IngestionWorker : BackgroundService
{
    private readonly ILogger<IngestionWorker> _logger;
    private readonly IngestionOptions _ingestion;
    private readonly IRecordPublisher _publisher;
    private readonly IStorageStrategyFactory _storageFactory;
    private readonly Iso25012Validator _validator;
    private readonly IOllamaAuditClient _ollama;
    private readonly IPipelineStateStore _state;
    private readonly ResiliencePipelineProvider<string> _pipelines;

    public IngestionWorker(
        ILogger<IngestionWorker> logger,
        IOptions<IngestionOptions> ingestion,
        IRecordPublisher publisher,
        IStorageStrategyFactory storageFactory,
        Iso25012Validator validator,
        IOllamaAuditClient ollama,
        IPipelineStateStore state,
        ResiliencePipelineProvider<string> pipelines)
    {
        _logger = logger;
        _ingestion = ingestion.Value;
        _publisher = publisher;
        _storageFactory = storageFactory;
        _validator = validator;
        _ollama = ollama;
        _state = state;
        _pipelines = pipelines;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var schemaPath = string.IsNullOrWhiteSpace(_ingestion.SchemaPath)
            ? Path.Combine(AppContext.BaseDirectory, "Schemas", "payment-instruction.json")
            : _ingestion.SchemaPath;

        if (!File.Exists(schemaPath))
        {
            schemaPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "FinGuardPlatform.CoreEngine", "Schemas", "payment-instruction.json"));
        }

        var parser = await FixedWidthParser.FromFileAsync(schemaPath, stoppingToken);
        var storage = _storageFactory.CreateFromConfiguration();
        var persistPipeline = storage.Target == StorageTarget.MongoDb
            ? _pipelines.GetPipeline(ResilienceRegistration.Mongo)
            : _pipelines.GetPipeline(ResilienceRegistration.Dynamo);

        _logger.LogInformation("FinGuard ingestion started. Schema={Schema} Storage={Storage}", parser.Schema.SchemaId, storage.Target);

        while (!stoppingToken.IsCancellationRequested)
        {
            var run = await _state.GetAsync(stoppingToken);
            run.RunId = Guid.NewGuid().ToString("N");
            run.LastStartedAt = DateTimeOffset.UtcNow;
            run.Status = "running";
            run.InputPath = _ingestion.InputPath;
            run.CleanPublished = 0;
            run.DeadLettered = 0;
            run.Persisted = 0;
            await _state.SaveAsync(run, stoppingToken);

            try
            {
                var inputPath = ResolveInputPath(_ingestion.InputPath);
                if (!File.Exists(inputPath))
                {
                    _logger.LogWarning("Input file {Path} not found; waiting.", inputPath);
                }
                else
                {
                    var text = await File.ReadAllTextAsync(inputPath, stoppingToken);
                    foreach (var result in parser.ParseText(text))
                    {
                        if (result.IsValid && result.Record is not null)
                        {
                            await _publisher.PublishCleanAsync(result.Record, stoppingToken);
                            await persistPipeline.ExecuteAsync(
                                async ct => await storage.PersistCleanRecordAsync(result.Record, ct),
                                stoppingToken);
                            run.CleanPublished++;
                            run.Persisted++;

                            var comment = await _ollama.CommentAsync(result.Record, stoppingToken);
                            if (!string.IsNullOrWhiteSpace(comment))
                            {
                                await _publisher.PublishAuditCommentAsync(result.Record.CorrelationId, comment, stoppingToken);
                            }
                        }
                        else if (result.DeadLetter is not null)
                        {
                            await _publisher.PublishDeadLetterAsync(result.DeadLetter, stoppingToken);
                            await persistPipeline.ExecuteAsync(
                                async ct => await storage.PersistDeadLetterAsync(result.DeadLetter, ct),
                                stoppingToken);
                            run.DeadLettered++;
                        }
                    }

                    foreach (var finding in _validator.EvaluateDataset(parser.Schema, (int)run.CleanPublished, (int)run.DeadLettered))
                    {
                        _logger.LogInformation("ISO 25012 {Dimension}: {Passed} ({Message})", finding.Dimension, finding.Passed, finding.Message);
                    }
                }

                run.Status = "idle";
                run.LastCompletedAt = DateTimeOffset.UtcNow;
                run.LastError = null;
            }
            catch (Exception ex)
            {
                run.Status = "failed";
                run.LastError = ex.Message;
                _logger.LogError(ex, "Ingestion cycle failed.");
            }

            await _state.SaveAsync(run, stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _ingestion.WatchIntervalSeconds)), stoppingToken);
        }
    }

    private static string ResolveInputPath(string configured)
    {
        if (Path.IsPathRooted(configured) && File.Exists(configured))
        {
            return configured;
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, configured),
            Path.GetFullPath(configured)
        };
        return candidates.FirstOrDefault(File.Exists) ?? configured;
    }
}
