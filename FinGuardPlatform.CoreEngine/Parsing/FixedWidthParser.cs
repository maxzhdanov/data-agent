using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using FinGuardPlatform.CoreEngine.Contracts;

namespace FinGuardPlatform.CoreEngine.Parsing;

public sealed class FixedWidthParser
{
    public FixedWidthSchema Schema { get; }

    public FixedWidthParser(FixedWidthSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (schema.Fields.Count == 0)
        {
            throw new InvalidOperationException($"Schema '{schema.SchemaId}' has no fields.");
        }

        Schema = schema;
    }

    public static FixedWidthParser FromJson(string json)
    {
        var schema = JsonSerializer.Deserialize<FixedWidthSchema>(json, JsonOptions)
            ?? throw new InvalidOperationException("Schema JSON deserialized to null.");
        return new FixedWidthParser(schema);
    }

    public static async Task<FixedWidthParser> FromFileAsync(string path, CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return FromJson(json);
    }

    public ParseResult ParseLine(string line, int lineNumber)
    {
        var errors = new List<string>();
        var findings = new List<QualityFinding>();
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (Schema.RecordLength > 0 && line.Length != Schema.RecordLength)
        {
            errors.Add($"Line {lineNumber} length {line.Length} does not match schema length {Schema.RecordLength}.");
            findings.Add(Fail(Iso25012Dimension.Completeness, "Record length mismatch."));
        }

        foreach (var field in Schema.Fields)
        {
            if (field.Start < 0 || field.Length <= 0 || field.Start + field.Length > line.Length)
            {
                errors.Add($"Field '{field.Name}' slice [{field.Start},{field.Length}] is outside line bounds ({line.Length}).");
                findings.Add(Fail(Iso25012Dimension.Completeness, $"Missing slice for {field.Name}."));
                continue;
            }

            var raw = line.Substring(field.Start, field.Length);
            var value = raw.Trim();
            var coerced = value;

            if (field.Required && string.IsNullOrWhiteSpace(value))
            {
                errors.Add($"Required field '{field.Name}' is empty.");
                findings.Add(Fail(Iso25012Dimension.Completeness, $"{field.Name} is required."));
            }

            if (!string.IsNullOrEmpty(value) && !TryCoerce(field, value, out coerced, out var typeError))
            {
                errors.Add(typeError);
                findings.Add(Fail(Iso25012Dimension.Accuracy, typeError));
                coerced = value;
            }

            if (!string.IsNullOrEmpty(field.Pattern) && !string.IsNullOrEmpty(value) &&
                !Regex.IsMatch(value, field.Pattern))
            {
                var message = $"Field '{field.Name}' does not match pattern.";
                errors.Add(message);
                findings.Add(Fail(Iso25012Dimension.Accuracy, message));
            }

            fields[field.Name] = coerced ?? string.Empty;

            foreach (var dimension in field.QualityDimensions)
            {
                if (findings.All(f => f.Dimension != dimension || f.Passed))
                {
                    findings.Add(Pass(dimension, $"{field.Name} satisfied {dimension}."));
                }
            }
        }

        findings.Add(Pass(Iso25012Dimension.Traceability, $"Line {lineNumber} bound to schema {Schema.SchemaId}@{Schema.Version}."));

        if (errors.Count > 0)
        {
            return new ParseResult
            {
                IsValid = false,
                DeadLetter = new DeadLetterPayload
                {
                    SchemaId = Schema.SchemaId,
                    LineNumber = lineNumber,
                    RawLine = line,
                    Errors = errors,
                    Quality = findings
                }
            };
        }

        return new ParseResult
        {
            IsValid = true,
            Record = new CleanRecord
            {
                SchemaId = Schema.SchemaId,
                SchemaVersion = Schema.Version,
                LineNumber = lineNumber,
                Fields = fields,
                Quality = findings
            }
        };
    }

    public IEnumerable<ParseResult> ParseText(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            yield return ParseLine(lines[i], i + 1);
        }
    }

    private static bool TryCoerce(FixedWidthField field, string value, out string coerced, out string error)
    {
        coerced = value;
        error = string.Empty;
        var type = field.DataType.ToLowerInvariant();

        switch (type)
        {
            case "string":
            case "text":
                return true;
            case "int":
            case "integer":
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    return true;
                }

                error = $"Field '{field.Name}' is not an integer.";
                return false;
            case "decimal":
            case "number":
                if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                {
                    return true;
                }

                error = $"Field '{field.Name}' is not a decimal.";
                return false;
            case "date":
                if (DateOnly.TryParseExact(value, ["yyyyMMdd", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                {
                    return true;
                }

                error = $"Field '{field.Name}' is not a date (yyyyMMdd or yyyy-MM-dd).";
                return false;
            default:
                return true;
        }
    }

    private static QualityFinding Pass(Iso25012Dimension dimension, string message) =>
        new() { Dimension = dimension, Passed = true, Message = message };

    private static QualityFinding Fail(Iso25012Dimension dimension, string message) =>
        new() { Dimension = dimension, Passed = false, Message = message };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };
}
