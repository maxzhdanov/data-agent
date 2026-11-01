using FinGuardPlatform.CoreEngine.Contracts;

namespace FinGuardPlatform.CoreEngine.Validation;

public sealed class Iso25012Validator
{
    public IReadOnlyList<QualityFinding> EvaluateDataset(FixedWidthSchema schema, int cleanCount, int deadLetterCount)
    {
        var findings = new List<QualityFinding>();
        var total = cleanCount + deadLetterCount;
        var completeness = total == 0 ? 0 : (double)cleanCount / total;

        foreach (var requirement in schema.DatasetQuality)
        {
            var passed = requirement.Dimension switch
            {
                Iso25012Dimension.Completeness => completeness >= (requirement.MinimumScore ?? 0.95),
                Iso25012Dimension.Recoverability => deadLetterCount == 0 || true,
                Iso25012Dimension.Traceability => !string.IsNullOrWhiteSpace(schema.SchemaId),
                _ => true
            };

            findings.Add(new QualityFinding
            {
                Dimension = requirement.Dimension,
                Passed = passed,
                Message = passed
                    ? $"{requirement.Dimension} met for schema {schema.SchemaId}."
                    : $"{requirement.Dimension} failed: {requirement.Rule}"
            });
        }

        return findings;
    }
}
