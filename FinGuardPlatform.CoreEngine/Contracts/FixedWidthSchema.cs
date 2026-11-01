namespace FinGuardPlatform.CoreEngine.Contracts;

public sealed class FixedWidthSchema
{
    public string SchemaId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0";
    public string Encoding { get; set; } = "utf-8";
    public int RecordLength { get; set; }
    public List<FixedWidthField> Fields { get; set; } = [];
    public List<QualityDimensionRequirement> DatasetQuality { get; set; } = [];
}

public sealed class FixedWidthField
{
    public string Name { get; set; } = string.Empty;
    public int Start { get; set; }
    public int Length { get; set; }
    public string DataType { get; set; } = "string";
    public bool Required { get; set; } = true;
    public string? Pattern { get; set; }
    public string? Description { get; set; }

    public List<Iso25012Dimension> QualityDimensions { get; set; } = [];
}
