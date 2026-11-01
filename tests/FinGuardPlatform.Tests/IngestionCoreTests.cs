using FinGuardPlatform.CoreEngine.Contracts;
using FinGuardPlatform.CoreEngine.Parsing;
using FinGuardPlatform.CoreEngine.Validation;

namespace FinGuardPlatform.Tests;

public sealed class IngestionCoreTests
{
    [Fact]
    public void CleanExampleRecordsParseSuccessfully()
    {
        var parser = LoadParser();
        var path = Path.Combine(AppContext.BaseDirectory, "samples", "payments-clean.fw");
        var results = parser.ParseText(File.ReadAllText(path)).ToArray();

        Assert.Equal(3, results.Length);
        Assert.All(results, result => Assert.True(result.IsValid));
        Assert.Equal("TEST000000000001", results[0].Record!.Fields["AccountId"]);
    }

    [Fact]
    public void MalformedExamplesAreRoutedToDeadLetter()
    {
        var parser = LoadParser();
        var path = Path.Combine(AppContext.BaseDirectory, "samples", "payments-malformed.fw");
        var results = parser.ParseText(File.ReadAllText(path)).ToArray();

        Assert.Equal(4, results.Length);
        Assert.All(results, result => Assert.False(result.IsValid));
        Assert.All(results, result => Assert.NotEmpty(result.DeadLetter!.Errors));
    }

    [Fact]
    public void SchemaRejectsOverlappingFields()
    {
        var schema = new FixedWidthSchema
        {
            SchemaId = "overlap-test",
            RecordLength = 8,
            Fields =
            [
                new FixedWidthField { Name = "left", Start = 0, Length = 5 },
                new FixedWidthField { Name = "right", Start = 4, Length = 4 }
            ]
        };

        var error = Assert.Throws<InvalidOperationException>(() => new FixedWidthParser(schema));
        Assert.Contains("overlap", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SchemaRejectsInvalidRegularExpression()
    {
        var schema = new FixedWidthSchema
        {
            SchemaId = "bad-pattern-test",
            RecordLength = 8,
            Fields = [new FixedWidthField { Name = "field", Start = 0, Length = 8, Pattern = "[" }]
        };

        Assert.Throws<InvalidOperationException>(() => new FixedWidthParser(schema));
    }

    [Fact]
    public void DatasetRecoveryFindingRequiresEveryDeadLetterToBePersisted()
    {
        var schema = LoadParser().Schema;
        var validator = new Iso25012Validator();

        var failed = validator.EvaluateDataset(schema, cleanCount: 9, deadLetterCount: 1, deadLettersPersisted: 0);
        var passed = validator.EvaluateDataset(schema, cleanCount: 9, deadLetterCount: 1, deadLettersPersisted: 1);

        Assert.Contains(failed, finding => finding.Dimension == Iso25012Dimension.Recoverability && !finding.Passed);
        Assert.Contains(passed, finding => finding.Dimension == Iso25012Dimension.Recoverability && finding.Passed);
    }

    [Fact]
    public void PatternValidationUsesTheSchemaContract()
    {
        var parser = LoadParser();
        var result = parser.ParseLine(new string(' ', parser.Schema.RecordLength), 1);

        Assert.False(result.IsValid);
        Assert.Contains(result.DeadLetter!.Errors, error => error.Contains("RecordType", StringComparison.Ordinal));
    }

    private static FixedWidthParser LoadParser()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Schemas", "payment-instruction.json");
        return FixedWidthParser.FromJson(File.ReadAllText(path));
    }
}
