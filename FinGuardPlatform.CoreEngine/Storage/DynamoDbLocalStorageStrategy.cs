using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using FinGuardPlatform.CoreEngine.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinGuardPlatform.CoreEngine.Storage;

public sealed class DynamoDbLocalStorageStrategy : IStorageStrategy, IDisposable
{
    private readonly IAmazonDynamoDB _client;
    private readonly string _tableName;
    private readonly ILogger<DynamoDbLocalStorageStrategy> _logger;
    private bool _tableEnsured;

    public StorageTarget Target => StorageTarget.DynamoDbLocal;

    public DynamoDbLocalStorageStrategy(IOptions<StorageOptions> options, ILogger<DynamoDbLocalStorageStrategy> logger)
    {
        _logger = logger;
        var dynamo = options.Value.Dynamo;
        _tableName = dynamo.TableName;
        var config = new AmazonDynamoDBConfig
        {
            ServiceURL = dynamo.ServiceUrl,
            AuthenticationRegion = dynamo.Region,
            UseHttp = dynamo.ServiceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        };
        _client = new AmazonDynamoDBClient(
            new BasicAWSCredentials(dynamo.AccessKey, dynamo.SecretKey),
            config);
    }

    public async Task PersistCleanRecordAsync(CleanRecord record, CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);
        var item = new Dictionary<string, AttributeValue>
        {
            ["pk"] = new(record.CorrelationId),
            ["schemaId"] = new(record.SchemaId),
            ["schemaVersion"] = new(record.SchemaVersion),
            ["lineNumber"] = new() { N = record.LineNumber.ToString() },
            ["ingestedAt"] = new(record.IngestedAt.ToString("O")),
            ["payload"] = new(System.Text.Json.JsonSerializer.Serialize(record.Fields)),
            ["storage"] = new("dynamodb-local")
        };
        _logger.LogDebug("Persisting clean record {Id} to DynamoDB Local", record.CorrelationId);
        await _client.PutItemAsync(new PutItemRequest
        {
            TableName = _tableName,
            Item = item
        }, cancellationToken);
    }

    public async Task PersistDeadLetterAsync(DeadLetterPayload payload, CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);
        await _client.PutItemAsync(new PutItemRequest
        {
            TableName = $"{_tableName}_dlq",
            Item = new Dictionary<string, AttributeValue>
            {
                ["pk"] = new(payload.CorrelationId),
                ["schemaId"] = new(payload.SchemaId),
                ["rawLine"] = new(payload.RawLine),
                ["errors"] = new(string.Join(" | ", payload.Errors))
            }
        }, cancellationToken);
    }

    private async Task EnsureTableAsync(CancellationToken cancellationToken)
    {
        if (_tableEnsured)
        {
            return;
        }

        var existing = await _client.ListTablesAsync(cancellationToken);
        foreach (var name in new[] { _tableName, $"{_tableName}_dlq" })
        {
            if (existing.TableNames.Contains(name))
            {
                continue;
            }

            await _client.CreateTableAsync(new CreateTableRequest
            {
                TableName = name,
                BillingMode = BillingMode.PAY_PER_REQUEST,
                AttributeDefinitions = [new AttributeDefinition("pk", ScalarAttributeType.S)],
                KeySchema = [new KeySchemaElement("pk", KeyType.HASH)]
            }, cancellationToken);
        }

        foreach (var name in new[] { _tableName, $"{_tableName}_dlq" })
        {
            var ready = false;
            for (var attempt = 0; attempt < 50 && !ready; attempt++)
            {
                try
                {
                    var description = await _client.DescribeTableAsync(name, cancellationToken);
                    ready = string.Equals(description.Table.TableStatus, "ACTIVE", StringComparison.Ordinal);
                }
                catch (ResourceNotFoundException) when (attempt < 49)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
                }
            }

            if (!ready)
            {
                throw new TimeoutException($"DynamoDB Local table '{name}' did not become active.");
            }
        }

        _tableEnsured = true;
    }

    public void Dispose() => _client.Dispose();
}
