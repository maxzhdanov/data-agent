namespace FinGuardPlatform.CoreEngine.Rag;

public sealed class AuditDocumentChunk
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Collection { get; set; } = "iso-audit-rules";
    public string Source { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public IReadOnlyDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
}

public sealed class SemanticHit
{
    public required AuditDocumentChunk Chunk { get; init; }
    public double Distance { get; init; }
}

public interface IVectorStore
{
    Task UpsertAsync(IEnumerable<AuditDocumentChunk> chunks, CancellationToken cancellationToken);
    Task<IReadOnlyList<SemanticHit>> QueryAsync(string collection, string query, int topK, CancellationToken cancellationToken);
}

public interface IAuditRuleRetriever
{
    Task<IReadOnlyList<SemanticHit>> RetrieveIsoRulesAsync(string question, int topK, CancellationToken cancellationToken);
}

public sealed class ChromaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:8000";
    public string Tenant { get; set; } = "default_tenant";
    public string Database { get; set; } = "default_database";
    public string Collection { get; set; } = "iso-audit-rules";
}
