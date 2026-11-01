using FinGuardPlatform.CoreEngine.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace FinGuardPlatform.CoreEngine.Storage;

public sealed class MongoDbStorageStrategy : IStorageStrategy
{
    private readonly IMongoCollection<BsonDocument> _clean;
    private readonly IMongoCollection<BsonDocument> _dlq;
    private readonly ILogger<MongoDbStorageStrategy> _logger;

    public StorageTarget Target => StorageTarget.MongoDb;

    public MongoDbStorageStrategy(IOptions<StorageOptions> options, ILogger<MongoDbStorageStrategy> logger)
    {
        _logger = logger;
        var mongo = options.Value.Mongo;
        var client = new MongoClient(mongo.ConnectionString);
        var db = client.GetDatabase(mongo.Database);
        _clean = db.GetCollection<BsonDocument>(options.Value.CollectionName);
        _dlq = db.GetCollection<BsonDocument>($"{options.Value.CollectionName}_dlq");
    }

    public Task PersistCleanRecordAsync(CleanRecord record, CancellationToken cancellationToken)
    {
        var document = new BsonDocument
        {
            ["_id"] = record.CorrelationId,
            ["schemaId"] = record.SchemaId,
            ["schemaVersion"] = record.SchemaVersion,
            ["lineNumber"] = record.LineNumber,
            ["ingestedAt"] = record.IngestedAt.UtcDateTime,
            ["fields"] = new BsonDocument(record.Fields.ToDictionary(k => k.Key, v => (BsonValue)v.Value)),
            ["storage"] = "mongodb-cosmos-api"
        };
        _logger.LogDebug("Persisting clean record {Id} to MongoDB", record.CorrelationId);
        return _clean.ReplaceOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", record.CorrelationId),
            document,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);
    }

    public Task PersistDeadLetterAsync(DeadLetterPayload payload, CancellationToken cancellationToken)
    {
        var document = new BsonDocument
        {
            ["_id"] = payload.CorrelationId,
            ["schemaId"] = payload.SchemaId,
            ["lineNumber"] = payload.LineNumber,
            ["rawLine"] = payload.RawLine,
            ["errors"] = new BsonArray(payload.Errors),
            ["failedAt"] = payload.FailedAt.UtcDateTime
        };
        return _dlq.ReplaceOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", payload.CorrelationId),
            document,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);
    }
}
