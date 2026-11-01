using System.Text.Json;
using System.Text.RegularExpressions;
using FinGuardPlatform.CoreEngine.Contracts;
using FinGuardPlatform.CoreEngine.Parsing;

namespace FinGuardPlatform.PipelineApp.Ingestion;

public sealed class SchemaCatalog
{
    private static readonly Regex SafeSchemaId = new("^[a-zA-Z0-9][a-zA-Z0-9._-]{0,99}$", RegexOptions.Compiled);
    private readonly string _builtInDirectory;
    private readonly string _customDirectory;

    public SchemaCatalog(IConfiguration configuration)
    {
        var dataDirectory = configuration["FinGuard:DataDirectory"] ?? Path.Combine(AppContext.BaseDirectory, "data");
        _builtInDirectory = Path.Combine(AppContext.BaseDirectory, "Schemas");
        _customDirectory = Path.Combine(dataDirectory, "schemas");
        Directory.CreateDirectory(_customDirectory);
    }

    public async Task<IReadOnlyList<FixedWidthSchema>> ListAsync(CancellationToken cancellationToken)
    {
        var schemas = new List<FixedWidthSchema>();
        foreach (var path in SchemaFiles())
        {
            schemas.Add(await ReadAsync(path, cancellationToken));
        }

        return schemas.OrderBy(schema => schema.SchemaId, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<FixedWidthSchema> ReadByIdAsync(string schemaId, CancellationToken cancellationToken)
    {
        if (!SafeSchemaId.IsMatch(schemaId))
        {
            throw new ArgumentException("Schema ID contains unsupported characters.", nameof(schemaId));
        }

        foreach (var path in SchemaFiles())
        {
            var schema = await ReadAsync(path, cancellationToken);
            if (string.Equals(schema.SchemaId, schemaId, StringComparison.OrdinalIgnoreCase))
            {
                return schema;
            }
        }

        throw new FileNotFoundException($"Schema '{schemaId}' was not found.");
    }

    public async Task SaveAsync(FixedWidthSchema schema, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (!SafeSchemaId.IsMatch(schema.SchemaId))
        {
            throw new ArgumentException("Schema ID must contain only letters, numbers, '.', '_' or '-'.");
        }

        _ = new FixedWidthParser(schema);
        var path = Path.Combine(_customDirectory, $"{schema.SchemaId}.json");
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
    }

    public async Task<string> GetPathByIdAsync(string schemaId, CancellationToken cancellationToken)
    {
        var schema = await ReadByIdAsync(schemaId, cancellationToken);
        var customPath = Path.Combine(_customDirectory, $"{schema.SchemaId}.json");
        if (File.Exists(customPath))
        {
            return customPath;
        }

        foreach (var path in SchemaFiles())
        {
            if (string.Equals(
                    (await ReadAsync(path, cancellationToken)).SchemaId,
                    schema.SchemaId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }
        }

        throw new FileNotFoundException($"Schema '{schemaId}' was not found.");
    }

    private IEnumerable<string> SchemaFiles()
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in new[] { _builtInDirectory, _customDirectory })
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
            {
                files[Path.GetFileNameWithoutExtension(path)] = path;
            }
        }

        return files.Values;
    }

    private static async Task<FixedWidthSchema> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var schema = JsonSerializer.Deserialize<FixedWidthSchema>(
                         await File.ReadAllTextAsync(path, cancellationToken),
                         new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip })
                     ?? throw new InvalidDataException($"Schema file '{Path.GetFileName(path)}' is empty.");
        _ = new FixedWidthParser(schema);
        return schema;
    }
}
