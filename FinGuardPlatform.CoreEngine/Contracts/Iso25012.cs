using System.Text.Json.Serialization;

namespace FinGuardPlatform.CoreEngine.Contracts;

/// <summary>
/// ISO/IEC 25012 data quality dimensions used as field and dataset metadata.
/// Names follow the standard; descriptions are original operational summaries, not copied clauses.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Iso25012Dimension
{
    Accuracy,
    Completeness,
    Consistency,
    Credibility,
    Currentness,
    Accessibility,
    Compliance,
    Confidentiality,
    Efficiency,
    Precision,
    Traceability,
    Understandability,
    Availability,
    Portability,
    Recoverability
}

public sealed class QualityDimensionRequirement
{
    public Iso25012Dimension Dimension { get; set; }
    public bool Required { get; set; } = true;
    public string Rule { get; set; } = string.Empty;
    public double? MinimumScore { get; set; }
}

public static class Iso25012Catalog
{
    public static IReadOnlyDictionary<Iso25012Dimension, string> OperationalSummaries { get; } =
        new Dictionary<Iso25012Dimension, string>
        {
            [Iso25012Dimension.Accuracy] = "Values match the real-world entity they represent within the declared format and domain.",
            [Iso25012Dimension.Completeness] = "Required attributes and records are present for the intended audit use.",
            [Iso25012Dimension.Consistency] = "The same fact does not conflict across fields, files, or downstream stores.",
            [Iso25012Dimension.Credibility] = "Source system and lineage are trusted enough for regulatory review.",
            [Iso25012Dimension.Currentness] = "The observation is fresh relative to the business as-of timestamp.",
            [Iso25012Dimension.Accessibility] = "Authorized pipeline services can retrieve the data in the expected encoding.",
            [Iso25012Dimension.Compliance] = "Retention, masking, and reporting rules required by policy are attached.",
            [Iso25012Dimension.Confidentiality] = "Sensitive attributes are classified and protected in transit and at rest.",
            [Iso25012Dimension.Efficiency] = "Payload size and parse cost stay within local hardware budgets.",
            [Iso25012Dimension.Precision] = "Numeric scale and rounding match the schema contract.",
            [Iso25012Dimension.Traceability] = "Each record can be tied to file, offset, schema version, and processing run.",
            [Iso25012Dimension.Understandability] = "Field names and coded values are documented for auditors and agents.",
            [Iso25012Dimension.Availability] = "Published topics and stores remain reachable for consumers.",
            [Iso25012Dimension.Portability] = "The schema can move across MongoDB/Cosmos and DynamoDB Local without semantic loss.",
            [Iso25012Dimension.Recoverability] = "Failed records are reconstructable from the dead-letter payload."
        };
}
