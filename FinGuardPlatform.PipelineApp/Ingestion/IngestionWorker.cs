using System.Collections.Concurrent;
using System.Threading.Channels;
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
using Polly.Registry;

namespace FinGuardPlatform.PipelineApp.Ingestion;

public sealed class IngestionWorker : BackgroundService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".fw", ".txt", ".dat"
    };

    private readonly ILogger<IngestionWorker> _logger;
    private readonly long _maximumUploadBytes;
    private readonly string _importsDirectory;
    private readonly IRecordPublisher _publisher;
    private readonly IStorageStrategyFactory _storageFactory;
    private readonly Iso25012Validator _validator;
    private readonly IOllamaAuditClient _ollama;
    private readonly IPipelineStateStore _stateStore;
    private readonly ResiliencePipelineProvider<string> _pipelines;
    private readonly SchemaCatalog _schemas;
    private readonly Channel<ImportRequest> _queue = Channel.CreateBounded<ImportRequest>(
        new BoundedChannelOptions(8) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false });
    private readonly ConcurrentDictionary<string, PipelineRunState> _runs = new(StringComparer.Ordinal);

    public IngestionWorker(
        ILogger<IngestionWorker> logger,
        IConfiguration configuration,
        IOptions<IngestionOptions> ingestion,
        IRecordPublisher publisher,
        IStorageStrategyFactory storageFactory,
        Iso25012Validator validator,
        IOllamaAuditClient ollama,
        IPipelineStateStore stateStore,
        ResiliencePipelineProvider<string> pipelines,
        SchemaCatalog schemas)
    {
        _logger = logger;
        _maximumUploadBytes = ingestion.Value.MaximumUploadBytes;
        _publisher = publisher;
        _storageFactory = storageFactory;
        _validator = validator;
        _ollama = ollama;
        _stateStore = stateStore;
        _pipelines = pipelines;
        _schemas = schemas;
        _importsDirectory = Path.Combine(
            configuration["FinGuard:DataDirectory"] ?? Path.Combine(AppContext.BaseDirectory, "data"),
            "imports");
        Directory.CreateDirectory(_importsDirectory);
    }

    public IReadOnlyList<PipelineRunState> GetRuns() =>
        _runs.Values.OrderByDescending(run => run.LastStartedAt).ToArray();

    public bool TryGetRun(string runId, out PipelineRunState? run) => _runs.TryGetValue(runId, out run);

    public async Task<PipelineRunState> QueueImportAsync(
        Stream content,
        string fileName,
        string schemaId,
        StorageTarget storageTarget,
        CancellationToken cancellationToken)
    {
        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName) || !SupportedExtensions.Contains(Path.GetExtension(safeFileName)))
        {
            throw new ArgumentException("Only .fw, .txt, and .dat fixed-width files are supported.");
        }

        _ = await _schemas.ReadByIdAsync(schemaId, cancellationToken);
        var runId = Guid.NewGuid().ToString("N");
        var path = Path.Combine(_importsDirectory, $"{runId}-{safeFileName}");

        try
        {
            await using var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var buffer = new byte[81920];
            long totalBytes = 0;
            int read;
            while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
            {
                totalBytes += read;
                if (totalBytes > _maximumUploadBytes)
                {
                    throw new ArgumentException($"Upload exceeds the {_maximumUploadBytes / (1024 * 1024)} MB limit.");
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        catch
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            throw;
        }

        var run = new PipelineRunState
        {
            RunId = runId,
            Status = "queued",
            Stage = "queued",
            SchemaId = schemaId,
            StorageTarget = storageTarget.ToString(),
            InputPath = safeFileName
        };
        _runs[runId] = run;

        try
        {
            await _queue.Writer.WriteAsync(new ImportRequest(runId, path, schemaId, storageTarget), cancellationToken);
        }
        catch
        {
            _runs.TryRemove(runId, out _);
            File.Delete(path);
            throw;
        }

        return run;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var previousRun = await _stateStore.GetAsync(stoppingToken);
        if (previousRun.Status is "running" or "queued")
        {
            previousRun.Status = "failed";
            previousRun.Stage = "interrupted";
            previousRun.LastError = "Pipeline service restarted before the import completed.";
            previousRun.LastCompletedAt = DateTimeOffset.UtcNow;
            await _stateStore.SaveAsync(previousRun, stoppingToken);
        }

        if (previousRun.Status != "idle")
        {
            _runs[previousRun.RunId] = previousRun;
        }

        await foreach (var request in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            await ProcessImportAsync(request, stoppingToken);
        }
    }

    private async Task ProcessImportAsync(ImportRequest request, CancellationToken cancellationToken)
    {
        var run = _runs[request.RunId];
        run.Status = "running";
        run.Stage = "loading schema";
        run.LastStartedAt = DateTimeOffset.UtcNow;
        await _stateStore.SaveAsync(run, cancellationToken);

        try
        {
            var parser = await FixedWidthParser.FromFileAsync(
                await _schemas.GetPathByIdAsync(request.SchemaId, cancellationToken),
                cancellationToken);
            var storage = _storageFactory.Create(request.StorageTarget);
            var persistPipeline = storage.Target == StorageTarget.MongoDb
                ? _pipelines.GetPipeline(ResilienceRegistration.Mongo)
                : _pipelines.GetPipeline(ResilienceRegistration.Dynamo);
            var text = await File.ReadAllTextAsync(request.FilePath, cancellationToken);
            var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n')
                .Select((line, index) => (line, index))
                .Where(entry => !string.IsNullOrWhiteSpace(entry.line))
                .ToArray();

            run.TotalRows = lines.LongLength;
            run.Stage = "parsing and validating";
            await SaveProgressAsync(run, cancellationToken);

            foreach (var (line, index) in lines)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = parser.ParseLine(line, index + 1);
                if (result.IsValid && result.Record is not null)
                {
                    run.Stage = "publishing and persisting clean record";
                    await _publisher.PublishCleanAsync(result.Record, cancellationToken);
                    await persistPipeline.ExecuteAsync(
                        async ct => await storage.PersistCleanRecordAsync(result.Record, ct),
                        cancellationToken);
                    run.CleanPublished++;
                    run.Persisted++;
                    run.ProcessedRows++;

                    run.Stage = "local AI audit";
                    await SaveProgressAsync(run, cancellationToken);
                    var comment = await _ollama.CommentAsync(result.Record, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(comment))
                    {
                        await _publisher.PublishAuditCommentAsync(result.Record.CorrelationId, comment, cancellationToken);
                        run.AuditComments = run.AuditComments.Append(comment).ToArray();
                    }

                    run.QualityFindings = run.QualityFindings
                        .Concat(result.Record.Quality)
                        .GroupBy(finding => (finding.Dimension, finding.Passed, finding.Message))
                        .Select(group => group.First())
                        .ToArray();
                }
                else if (result.DeadLetter is not null)
                {
                    run.Stage = "publishing and persisting dead letter";
                    await _publisher.PublishDeadLetterAsync(result.DeadLetter, cancellationToken);
                    await persistPipeline.ExecuteAsync(
                        async ct => await storage.PersistDeadLetterAsync(result.DeadLetter, ct),
                        cancellationToken);
                    run.DeadLettered++;
                    run.DeadLettersPersisted++;
                    run.Persisted++;
                    run.ProcessedRows++;
                    run.QualityFindings = run.QualityFindings
                        .Concat(result.DeadLetter.Quality)
                        .GroupBy(finding => (finding.Dimension, finding.Passed, finding.Message))
                        .Select(group => group.First())
                        .ToArray();

                    run.Stage = "local AI audit";
                    await SaveProgressAsync(run, cancellationToken);
                    var comment = await _ollama.CommentOnDeadLetterAsync(result.DeadLetter, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(comment))
                    {
                        await _publisher.PublishAuditCommentAsync(result.DeadLetter.CorrelationId, comment, cancellationToken);
                        run.AuditComments = run.AuditComments.Append(comment).ToArray();
                    }
                }

                await SaveProgressAsync(run, cancellationToken);
            }

            run.QualityFindings = run.QualityFindings.Concat(
                    _validator.EvaluateDataset(
                        parser.Schema,
                        (int)run.CleanPublished,
                        (int)run.DeadLettered,
                        (int)run.DeadLettersPersisted))
                .GroupBy(finding => (finding.Dimension, finding.Passed, finding.Message))
                .Select(group => group.First())
                .ToArray();
            run.Status = "completed";
            run.Stage = "complete";
            run.LastError = null;
            BuildReport(run);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            run.Status = "cancelled";
            run.Stage = "cancelled";
            throw;
        }
        catch (Exception exception)
        {
            run.Status = "failed";
            run.Stage = "failed";
            run.LastError = exception.Message;
            BuildReport(run);
            _logger.LogError(exception, "Import run {RunId} failed at stage {Stage}.", run.RunId, run.Stage);
        }
        finally
        {
            run.LastCompletedAt = DateTimeOffset.UtcNow;
            await SaveProgressAsync(run, CancellationToken.None);
            try
            {
                File.Delete(request.FilePath);
            }
            catch (IOException exception)
            {
                _logger.LogWarning(exception, "Could not remove temporary import file for run {RunId}.", run.RunId);
            }
        }
    }

    private async Task SaveProgressAsync(PipelineRunState run, CancellationToken cancellationToken) =>
        await _stateStore.SaveAsync(run, cancellationToken);

    private static void BuildReport(PipelineRunState run)
    {
        run.FileHealthScore = run.TotalRows == 0
            ? 0
            : (int)Math.Round((double)run.CleanPublished / run.TotalRows * 100);
        run.PrimaryFailureReason = run.QualityFindings.FirstOrDefault(finding => !finding.Passed)?.Message
                                   ?? (run.LastError is null ? "No validation failures." : run.LastError);
        run.ReportMarkdown = $"""
            # Import audit report

            - **Run:** `{run.RunId}`
            - **File:** `{run.InputPath}`
            - **Schema:** `{run.SchemaId}`
            - **Persistence:** `{run.StorageTarget}`
            - **Status:** `{run.Status}`
            - **File health score:** {run.FileHealthScore}/100
            - **Primary failure:** {run.PrimaryFailureReason}

            ## Processing summary

            - Rows: {run.ProcessedRows}/{run.TotalRows}
            - Clean: {run.CleanPublished}
            - Dead-lettered: {run.DeadLettered}
            - Persisted: {run.Persisted}

            ## ISO 25012 findings

            {string.Join("\n", run.QualityFindings.Select(finding => $"- [{(finding.Passed ? "PASS" : "FAIL")}] {finding.Dimension}: {finding.Message}"))}

            ## Local AI audit notes

            {(run.AuditComments.Count == 0 ? "- No AI comments were generated." : string.Join("\n", run.AuditComments.Select(comment => $"- {comment}")))}
            """;
    }

    private sealed record ImportRequest(string RunId, string FilePath, string SchemaId, StorageTarget StorageTarget);
}
