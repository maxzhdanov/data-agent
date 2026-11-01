using FinGuardPlatform.CoreEngine.Contracts;
using FinGuardPlatform.CoreEngine.Parsing;
using FinGuardPlatform.CoreEngine.State;
using FinGuardPlatform.McpServer;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddSingleton<IPipelineStateStore>(_ =>
{
    var path = builder.Configuration["PipelineStatePath"] ?? Path.Combine(AppContext.BaseDirectory, "data", "pipeline-state.json");
    return new FilePipelineStateStore(path);
});
builder.Services.AddSingleton<SchemaCatalog>();
builder.Services.AddSingleton<IsoRuleCatalog>();

var app = builder.Build();
app.MapDefaultEndpoints();

app.MapGet("/", () => Results.Json(new
{
    name = "FinGuardPlatform MCP Server",
    protocol = "jsonrpc-2.0",
    endpoint = "/mcp",
    tools = new[] { "list_schemas", "get_pipeline_state", "list_iso_rules" }
}));

app.MapPost("/mcp", async (HttpContext context, SchemaCatalog schemas, IPipelineStateStore state, IsoRuleCatalog rules) =>
{
    var rpc = await context.Request.ReadFromJsonAsync<JsonRpcRequest>();
    if (rpc is null)
    {
        return Results.Json(JsonRpcResponse.Error(null, -32700, "Parse error"));
    }

    try
    {
        object? result = rpc.Method switch
        {
            "initialize" => new
            {
                protocolVersion = "2024-11-05",
                serverInfo = new { name = "FinGuardPlatform.McpServer", version = "0.1.0" },
                capabilities = new { tools = new { }, resources = new { } }
            },
            "tools/list" => new { tools = McpTools.Descriptors },
            "tools/call" => await McpTools.CallAsync(rpc, schemas, state, rules, context.RequestAborted),
            "resources/list" => new
            {
                resources = new object[]
                {
                    new { uri = "finguard://schemas", name = "Database and file schemas" },
                    new { uri = "finguard://pipeline-state", name = "Pipeline run state" }
                }
            },
            "list_schemas" => schemas.List(),
            "get_pipeline_state" => await state.GetAsync(context.RequestAborted),
            "list_iso_rules" => rules.List(),
            _ => throw new InvalidOperationException($"Unknown method '{rpc.Method}'.")
        };

        return Results.Json(JsonRpcResponse.Success(rpc.Id, result));
    }
    catch (Exception ex)
    {
        return Results.Json(JsonRpcResponse.Error(rpc.Id, -32601, ex.Message));
    }
});

app.Run();

namespace FinGuardPlatform.McpServer
{
    public sealed record JsonRpcRequest(string Jsonrpc, string Method, object? Id, System.Text.Json.JsonElement? Params);

    public static class JsonRpcResponse
    {
        public static object Success(object? id, object? result) => new { jsonrpc = "2.0", id, result };
        public static object Error(object? id, int code, string message) => new { jsonrpc = "2.0", id, error = new { code, message } };
    }

    public sealed class SchemaCatalog
    {
        private readonly List<FixedWidthSchema> _schemas = [];

        public SchemaCatalog(IWebHostEnvironment env)
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "Schemas");
            if (!Directory.Exists(dir))
            {
                return;
            }

            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                _schemas.Add(FixedWidthParser.FromFileAsync(file).GetAwaiter().GetResult().Schema);
            }
        }

        public object List() => _schemas.Select(s => new
        {
            s.SchemaId,
            s.Name,
            s.Version,
            s.RecordLength,
            fields = s.Fields.Select(f => new { f.Name, f.Start, f.Length, f.DataType, f.Required, f.QualityDimensions })
        });
    }

    public sealed class IsoRuleCatalog
    {
        public IReadOnlyList<IsoRule> List()
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "iso-rules");
            if (!Directory.Exists(dir))
            {
                return [];
            }

            return Directory.GetFiles(dir, "*.md")
                .Select(path => new IsoRule(Path.GetFileNameWithoutExtension(path), File.ReadAllText(path)))
                .ToList();
        }
    }

    public sealed record IsoRule(string Id, string Text);

    public static class McpTools
    {
        public static readonly object[] Descriptors =
        [
            new { name = "list_schemas", description = "List fixed-width and persistence schemas known to FinGuard.", inputSchema = new { type = "object", properties = new { } } },
            new { name = "get_pipeline_state", description = "Return last ingestion run counts and DLQ depth placeholder.", inputSchema = new { type = "object", properties = new { } } },
            new { name = "list_iso_rules", description = "List seeded ISO 27001 / ISO 25012 operational rules for the local RAG agent.", inputSchema = new { type = "object", properties = new { } } }
        ];

        public static async Task<object> CallAsync(JsonRpcRequest rpc, SchemaCatalog schemas, IPipelineStateStore state, IsoRuleCatalog rules, CancellationToken cancellationToken)
        {
            var name = "list_schemas";
            if (rpc.Params is { } p && p.TryGetProperty("name", out var nameNode))
            {
                name = nameNode.GetString() ?? name;
            }

            object payload = name switch
            {
                "get_pipeline_state" => await state.GetAsync(cancellationToken),
                "list_iso_rules" => rules.List(),
                _ => schemas.List()
            };

            return new
            {
                content = new[] { new { type = "text", text = System.Text.Json.JsonSerializer.Serialize(payload) } }
            };
        }
    }
}
