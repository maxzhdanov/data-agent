using FinGuardPlatform.CoreEngine.Contracts;

namespace FinGuardPlatform.CoreEngine.Storage;

public enum StorageTarget
{
    MongoDb = 0,
    DynamoDbLocal = 1
}

public sealed class StorageOptions
{
    public StorageTarget Target { get; set; } = StorageTarget.MongoDb;
    public string CollectionName { get; set; } = "clean_records";
    public MongoOptions Mongo { get; set; } = new();
    public DynamoOptions Dynamo { get; set; } = new();
}

public sealed class MongoOptions
{
    public string ConnectionString { get; set; } = "mongodb://localhost:27017";
    public string Database { get; set; } = "finguard";
}

public sealed class DynamoOptions
{
    public string ServiceUrl { get; set; } = "http://localhost:8001";
    public string TableName { get; set; } = "finguard_clean_records";
    public string Region { get; set; } = "us-east-1";
    public string AccessKey { get; set; } = "local";
    public string SecretKey { get; set; } = "local";
}

public interface IStorageStrategy
{
    StorageTarget Target { get; }
    Task PersistCleanRecordAsync(CleanRecord record, CancellationToken cancellationToken);
    Task PersistDeadLetterAsync(DeadLetterPayload payload, CancellationToken cancellationToken);
}

public interface IDocumentStore : IStorageStrategy;

public interface IStorageStrategyFactory
{
    IStorageStrategy Create(StorageTarget? target = null);
    IStorageStrategy CreateFromConfiguration();
}
