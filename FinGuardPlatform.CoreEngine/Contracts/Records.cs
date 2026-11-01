namespace FinGuardPlatform.CoreEngine.Contracts;

public sealed class ParseResult
{
    public bool IsValid { get; init; }
    public CleanRecord? Record { get; init; }
    public DeadLetterPayload? DeadLetter { get; init; }
}

public sealed class CleanRecord
{
    public required string SchemaId { get; init; }
    public required string SchemaVersion { get; init; }
    public required int LineNumber { get; init; }
    public required IReadOnlyDictionary<string, string> Fields { get; init; }
    public required IReadOnlyList<QualityFinding> Quality { get; init; }
    public DateTimeOffset IngestedAt { get; init; } = DateTimeOffset.UtcNow;
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N");
}

public sealed class DeadLetterPayload
{
    public required string SchemaId { get; init; }
    public required int LineNumber { get; init; }
    public required string RawLine { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public required IReadOnlyList<QualityFinding> Quality { get; init; }
    public DateTimeOffset FailedAt { get; init; } = DateTimeOffset.UtcNow;
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N");
}

public sealed class QualityFinding
{
    public Iso25012Dimension Dimension { get; init; }
    public bool Passed { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class PipelineEnvelope<T>
{
    public string Topic { get; init; } = string.Empty;
    public string MessageType { get; init; } = typeof(T).Name;
    public DateTimeOffset PublishedAt { get; init; } = DateTimeOffset.UtcNow;
    public T? Payload { get; init; }
}

public static class KafkaTopics
{
    public const string CleanRecords = "finguard.records.clean";
    public const string DeadLetter = "finguard.records.dlq";
    public const string AuditComments = "finguard.audit.comments";
    public const string PipelineState = "finguard.pipeline.state";
}

public sealed class PipelineRunState
{
    public string RunId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset? LastStartedAt { get; set; }
    public DateTimeOffset? LastCompletedAt { get; set; }
    public string Status { get; set; } = "idle";
    public string Stage { get; set; } = "idle";
    public string SchemaId { get; set; } = string.Empty;
    public string StorageTarget { get; set; } = string.Empty;
    public long TotalRows { get; set; }
    public long ProcessedRows { get; set; }
    public long CleanPublished { get; set; }
    public long DeadLettered { get; set; }
    public long DeadLettersPersisted { get; set; }
    public long Persisted { get; set; }
    public int? FileHealthScore { get; set; }
    public string PrimaryFailureReason { get; set; } = string.Empty;
    public string ReportMarkdown { get; set; } = string.Empty;
    public string? LastError { get; set; }
    public string InputPath { get; set; } = string.Empty;
    public IReadOnlyList<string> AuditComments { get; set; } = [];
    public long AuditFailures { get; set; }
    public IReadOnlyList<string> AuditErrors { get; set; } = [];
    public IReadOnlyList<QualityFinding> QualityFindings { get; set; } = [];
}
