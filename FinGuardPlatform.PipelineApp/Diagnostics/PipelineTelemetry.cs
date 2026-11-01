using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace FinGuardPlatform.PipelineApp.Diagnostics;

public static class PipelineTelemetry
{
    public const string ActivitySourceName = "FinGuardPlatform.PipelineApp";
    public const string MeterName = "FinGuardPlatform.PipelineApp";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> ImportRunsStarted = Meter.CreateCounter<long>("finguard.imports.started");
    public static readonly Counter<long> ImportRunsCompleted = Meter.CreateCounter<long>("finguard.imports.completed");
    public static readonly Counter<long> ImportRunsFailed = Meter.CreateCounter<long>("finguard.imports.failed");
    public static readonly Counter<long> RowsProcessed = Meter.CreateCounter<long>("finguard.rows.processed");
    public static readonly Counter<long> CleanRecords = Meter.CreateCounter<long>("finguard.records.clean");
    public static readonly Counter<long> DeadLetters = Meter.CreateCounter<long>("finguard.records.dead_lettered");
    public static readonly Counter<long> AuditComments = Meter.CreateCounter<long>("finguard.audit.comments");
    public static readonly Counter<long> AuditFailures = Meter.CreateCounter<long>("finguard.audit.failures");
    public static readonly Histogram<double> ImportDuration = Meter.CreateHistogram<double>(
        "finguard.import.duration",
        unit: "ms");
}
