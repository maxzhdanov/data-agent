using FinGuardPlatform.CoreEngine.Rag;
using Polly.Timeout;

namespace FinGuardPlatform.PipelineApp.Rag;

public sealed class ChromaRuleSeeder : BackgroundService
{
    private readonly IVectorStore _store;
    private readonly ILogger<ChromaRuleSeeder> _logger;
    private readonly string _rulesDirectory = Path.Combine(AppContext.BaseDirectory, "iso-rules");

    public ChromaRuleSeeder(IVectorStore store, ILogger<ChromaRuleSeeder> logger)
    {
        _store = store;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var files = Directory.Exists(_rulesDirectory)
            ? Directory.EnumerateFiles(_rulesDirectory, "*.md").Order(StringComparer.OrdinalIgnoreCase).ToArray()
            : [];
        if (files.Length == 0)
        {
            _logger.LogWarning("No ISO rule documents were found at {RulesDirectory}.", _rulesDirectory);
            return;
        }

        var chunks = new List<AuditDocumentChunk>();
        foreach (var path in files)
        {
            var title = Path.GetFileName(path);
            chunks.Add(new AuditDocumentChunk
            {
                Id = Path.GetFileNameWithoutExtension(path),
                Source = title,
                Title = title,
                Text = await File.ReadAllTextAsync(path, stoppingToken),
                Metadata = new Dictionary<string, string> { ["kind"] = "iso-seed" }
            });
        }

        var delay = TimeSpan.FromSeconds(2);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _store.UpsertAsync(chunks, stoppingToken);
                _logger.LogInformation("Loaded {DocumentCount} ISO rule documents into ChromaDB.", chunks.Count);
                return;
            }
            catch (Exception exception) when (
                !stoppingToken.IsCancellationRequested &&
                (exception is HttpRequestException ||
                 exception is TaskCanceledException ||
                 exception is TimeoutRejectedException))
            {
                _logger.LogWarning(exception, "ChromaDB is not ready; ISO rule seeding will retry in {Delay}.", delay);
                await Task.Delay(delay, stoppingToken);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
        }
    }
}
